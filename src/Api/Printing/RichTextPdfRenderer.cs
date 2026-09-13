using ErpApp.Domain.Common;
using QuestPDF.Fluent;
using QuestPDF.Infrastructure;

namespace ErpApp.Api.Printing;

/// <summary>
/// Phase 39 -- draws a rich-text field into a QuestPDF column.
///
/// <para><b>This class is the reason <see cref="RichText"/>'s grammar is the size it is.</b> The
/// stored HTML and this renderer are two folds over one <see cref="RichTextNode"/> tree, so the
/// question "what formatting may a user apply?" has a single answer: whatever both a browser and
/// QuestPDF can show. A grammar wider than this file would mean a Terms block that looks one way on
/// the detail page and another in the PDF the customer actually receives -- and the PDF is the copy
/// that gets argued about.</para>
///
/// <para>QuestPDF has no HTML support and this does not add one. It switches on node types, which is
/// why there is no markup-shaped code here and no second parser to keep in step with the first.</para>
/// </summary>
public static class RichTextPdfRenderer
{
    /// <summary>
    /// Renders <paramref name="html"/> into <paramref name="container"/> at the given size, or draws
    /// nothing at all when the field is empty -- callers check emptiness before adding a heading, so
    /// an empty field never prints a bare "Terms and Conditions" label.
    /// </summary>
    public static void Render(IContainer container, string? html, float fontSize)
    {
        var blocks = RichText.Parse(html);

        if (blocks.Count == 0)
        {
            return;
        }

        container.Column(column =>
        {
            foreach (var block in blocks)
            {
                RenderBlock(column, block, fontSize);
            }
        });
    }

    private static void RenderBlock(ColumnDescriptor column, RichTextNode block, float fontSize)
    {
        switch (block)
        {
            case RichTextNode.Paragraph paragraph:
                if (paragraph.Children.Count == 0)
                {
                    // A deliberate blank line between two paragraphs. Drawn as vertical space rather
                    // than as an empty Text, which QuestPDF collapses to nothing.
                    column.Item().Height(fontSize * 0.8f);

                    return;
                }

                column.Item().Text(text =>
                {
                    text.DefaultTextStyle(style => style.FontSize(fontSize));
                    Align(text, paragraph.Alignment);
                    WriteInline(text, paragraph.Children, RichTextStyle.None);
                });
                break;

            case RichTextNode.List list:
                var index = 1;

                foreach (var listItem in list.Items)
                {
                    var marker = list.Ordered ? $"{index}." : "•";

                    // A row rather than a "• " prefix inside the text: a wrapped second line has to
                    // hang under the first word, not under the bullet.
                    column.Item().PaddingLeft(8).Row(row =>
                    {
                        row.ConstantItem(list.Ordered ? 18 : 12).Text(marker).FontSize(fontSize);
                        row.RelativeItem().Text(text =>
                        {
                            text.DefaultTextStyle(style => style.FontSize(fontSize));
                            WriteInline(text, listItem.Children, RichTextStyle.None);
                        });
                    });

                    index++;
                }

                break;

            case RichTextNode.Rule:
                column.Item().PaddingVertical(4).LineHorizontal(0.5f);
                break;
        }
    }

    /// <summary>
    /// Emphasis composes on the way down, so bold-inside-italic arrives at the text span as both --
    /// the same composition <c>Normalizer</c> does when it nests the tags, reached from the other
    /// side.
    /// </summary>
    private static void WriteInline(TextDescriptor text, IReadOnlyList<RichTextNode> nodes, RichTextStyle style)
    {
        foreach (var node in nodes)
        {
            switch (node)
            {
                case RichTextNode.Text run:
                    var span = text.Span(run.Value);

                    if (style.HasFlag(RichTextStyle.Bold))
                    {
                        span = span.Bold();
                    }

                    if (style.HasFlag(RichTextStyle.Italic))
                    {
                        span = span.Italic();
                    }

                    if (style.HasFlag(RichTextStyle.Underline))
                    {
                        span = span.Underline();
                    }

                    if (style.HasFlag(RichTextStyle.Strikethrough))
                    {
                        span.Strikethrough();
                    }

                    break;

                case RichTextNode.LineBreak:
                    text.Line(string.Empty);
                    break;

                case RichTextNode.Emphasis emphasis:
                    WriteInline(text, emphasis.Children, style | emphasis.Style);
                    break;
            }
        }
    }

    /// <summary>
    /// Alignment is applied to the text block, not to its container: QuestPDF exposes Justify on
    /// <see cref="TextDescriptor"/> only, and a justified paragraph is the one alignment a container
    /// cannot express at all.
    /// </summary>
    private static void Align(TextDescriptor text, RichTextAlignment alignment)
    {
        switch (alignment)
        {
            case RichTextAlignment.Center:
                text.AlignCenter();
                break;
            case RichTextAlignment.End:
                text.AlignRight();
                break;
            case RichTextAlignment.Justify:
                text.Justify();
                break;
            default:
                text.AlignLeft();
                break;
        }
    }
}
