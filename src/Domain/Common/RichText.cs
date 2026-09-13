using System.Globalization;
using System.Text;

namespace ErpApp.Domain.Common;

/// <summary>
/// Phase 39 -- the one place this codebase turns arbitrary HTML into a rich-text field it is willing
/// to store, print and email.
///
/// <para><b>Why a sanitiser is not optional here.</b> The reference product's editor is TinyMCE
/// 7.1.1 with a <c>code</c> ("Source code") button and <i>no</i> <c>valid_elements</c> configured --
/// both read off the live tenant on 2026-09-13. A user can therefore type raw markup by hand, and
/// what they type is rendered later on a document detail page, in a PDF a customer receives, and in
/// an HTML email body. Three render targets, one of which is a browser, means an unsanitised field
/// is a stored-XSS hole by construction.</para>
///
/// <para><b>The mechanism is re-emission, not filtering.</b> Nothing here asks "is this tag safe?"
/// or "is this attribute safe?" and passes the answer through. The input is parsed into
/// <see cref="RichTextNode"/> -- a tree with no attribute slot except an <i>enum</i>
/// (<see cref="RichTextAlignment"/>) -- and the output is generated from that tree out of string
/// constants this file owns, with every text run escaped on the way. The security property that
/// follows is worth stating precisely, because it is what makes a hand-written parser acceptable at
/// all: <b>a bug in the tokenizer can produce wrong formatting, and cannot produce an attribute, a
/// tag name or a URL that came from the input.</b> The only user bytes that survive are text-node
/// characters, and those are escaped.</para>
///
/// <para><b>Where it runs.</b> On write, in the Domain -- <c>Invoice.SetTerms</c> and its four
/// siblings, <c>CustomTemplate</c>, <c>EmailTemplate</c> and the send path all call
/// <see cref="Sanitize"/> before storing. Sanitising on read would mean every future read path has
/// to remember (phase-35a's lesson is that read paths are exactly what a sweep forgets), and
/// sanitising in a handler would leave a Domain caller able to bypass it. Stored content is
/// therefore already safe, and <see cref="Sanitize"/> is idempotent so re-saving never
/// double-escapes.</para>
/// </summary>
public static class RichText
{
    /// <summary>
    /// Upper bound on the <i>sanitised</i> output, in characters. Generous: a page of terms and
    /// conditions with markup runs to a few thousand. It exists so a paste of an entire web page
    /// cannot put an unbounded string into a column, not to police prose length.
    /// </summary>
    public const int MaxLength = 64_000;

    /// <summary>
    /// Canonical, safe HTML for <paramref name="html"/>, or null when it carries no content.
    ///
    /// <para>Idempotent: <c>Sanitize(Sanitize(x)) == Sanitize(x)</c> for every input. That matters
    /// in practice rather than in theory -- a document is loaded, shown in the editor and saved
    /// again on every edit, so a non-idempotent sanitiser would visibly rot a field over a few
    /// saves.</para>
    /// </summary>
    public static string? Sanitize(string? html)
    {
        var blocks = Parse(html);

        if (blocks.Count == 0)
        {
            return null;
        }

        var builder = new StringBuilder();
        WriteBlocks(builder, blocks);
        var result = builder.ToString();

        return result.Length == 0 ? null : result;
    }

    /// <summary>
    /// The tree behind <paramref name="html"/>: a list of block-level nodes. Public because the
    /// print pipeline folds over the same tree the stored HTML is emitted from -- that shared fold
    /// is what keeps the screen and the PDF from disagreeing.
    /// </summary>
    public static IReadOnlyList<RichTextNode> Parse(string? html)
    {
        if (string.IsNullOrWhiteSpace(html))
        {
            return [];
        }

        var raw = RawParser.Parse(html);
        var blocks = Normalizer.ToBlocks(raw);

        return Trim(blocks);
    }

    /// <summary>
    /// The field's text with all markup removed, for a spreadsheet cell, a list preview or a search
    /// index -- anywhere a single string is wanted and markup would be noise. List items keep a
    /// bullet so a flattened list still reads as one.
    /// </summary>
    public static string ToPlainText(string? html)
    {
        var lines = new List<string>();
        WritePlainBlocks(Parse(html), lines);

        return string.Join("\n", lines).Trim();
    }

    /// <summary>True when the field would store nothing -- markup with no text, whitespace, or null.
    /// A validator wanting "terms were actually entered" asks this, never <c>string.IsNullOrEmpty</c>
    /// on the raw input, which <c>&lt;p&gt;&lt;br&gt;&lt;/p&gt;</c> would pass.</summary>
    public static bool IsEmpty(string? html) => Sanitize(html) is null;

    /// <summary>
    /// Wraps plain text as rich text, escaping it and turning blank-line-separated runs into
    /// paragraphs and single newlines into breaks.
    ///
    /// <para>This is how the phase-39 migration converts the plain-text <c>Terms</c> that phases 27b
    /// and 20d stored. Detecting "is this row HTML or not?" at render time would have left two
    /// representations in one column forever; converting once means there is exactly one.</para>
    /// </summary>
    public static string? FromPlainText(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var normalized = text.Replace("\r\n", "\n").Replace('\r', '\n');
        var paragraphs = normalized.Split("\n\n", StringSplitOptions.None);
        var builder = new StringBuilder();

        foreach (var paragraph in paragraphs)
        {
            if (paragraph.Trim().Length == 0)
            {
                continue;
            }

            builder.Append("<p>");
            var first = true;

            foreach (var line in paragraph.Split('\n'))
            {
                if (!first)
                {
                    builder.Append("<br />");
                }

                Escape(builder, line);
                first = false;
            }

            builder.Append("</p>");
        }

        return builder.Length == 0 ? null : builder.ToString();
    }

    // --- emitting -----------------------------------------------------------------------------

    private static void WriteBlocks(StringBuilder builder, IReadOnlyList<RichTextNode> blocks)
    {
        foreach (var block in blocks)
        {
            switch (block)
            {
                case RichTextNode.Paragraph paragraph:
                    builder.Append(paragraph.Alignment == RichTextAlignment.Start
                        ? "<p>"
                        : $"<p style=\"text-align:{AlignmentCss(paragraph.Alignment)}\">");
                    WriteInline(builder, paragraph.Children);
                    builder.Append("</p>");
                    break;

                case RichTextNode.List list:
                    builder.Append(list.Ordered ? "<ol>" : "<ul>");
                    foreach (var item in list.Items)
                    {
                        builder.Append("<li>");
                        WriteInline(builder, item.Children);
                        builder.Append("</li>");
                    }

                    builder.Append(list.Ordered ? "</ol>" : "</ul>");
                    break;

                case RichTextNode.Rule:
                    builder.Append("<hr />");
                    break;
            }
        }
    }

    private static void WriteInline(StringBuilder builder, IReadOnlyList<RichTextNode> nodes)
    {
        foreach (var node in nodes)
        {
            switch (node)
            {
                case RichTextNode.Text text:
                    Escape(builder, text.Value);
                    break;

                case RichTextNode.LineBreak:
                    builder.Append("<br />");
                    break;

                case RichTextNode.Emphasis emphasis:
                    var tag = EmphasisTag(emphasis.Style);
                    builder.Append('<').Append(tag).Append('>');
                    WriteInline(builder, emphasis.Children);
                    builder.Append("</").Append(tag).Append('>');
                    break;
            }
        }
    }

    /// <summary>The four constants that are the entire tag vocabulary of an emphasis run.</summary>
    private static string EmphasisTag(RichTextStyle style) => style switch
    {
        RichTextStyle.Bold => "strong",
        RichTextStyle.Italic => "em",
        RichTextStyle.Underline => "u",
        _ => "s",
    };

    private static string AlignmentCss(RichTextAlignment alignment) => alignment switch
    {
        RichTextAlignment.Center => "center",
        RichTextAlignment.End => "right",
        RichTextAlignment.Justify => "justify",
        _ => "left",
    };

    private static void Escape(StringBuilder builder, string value)
    {
        foreach (var c in value)
        {
            switch (c)
            {
                case '&': builder.Append("&amp;"); break;
                case '<': builder.Append("&lt;"); break;
                case '>': builder.Append("&gt;"); break;
                case '"': builder.Append("&quot;"); break;
                case '\'': builder.Append("&#39;"); break;
                default: builder.Append(c); break;
            }
        }
    }

    // --- flattening ---------------------------------------------------------------------------

    private static void WritePlainBlocks(IReadOnlyList<RichTextNode> blocks, List<string> lines)
    {
        foreach (var block in blocks)
        {
            switch (block)
            {
                case RichTextNode.Paragraph paragraph:
                    lines.AddRange(PlainLines(paragraph.Children));
                    break;

                case RichTextNode.List list:
                    var index = 1;
                    foreach (var item in list.Items)
                    {
                        var prefix = list.Ordered
                            ? $"{index.ToString(CultureInfo.InvariantCulture)}. "
                            : "• ";
                        var itemLines = PlainLines(item.Children);
                        lines.Add(prefix + (itemLines.Count > 0 ? itemLines[0] : string.Empty));
                        for (var i = 1; i < itemLines.Count; i++)
                        {
                            lines.Add(new string(' ', prefix.Length) + itemLines[i]);
                        }

                        index++;
                    }

                    break;

                case RichTextNode.Rule:
                    lines.Add("---");
                    break;
            }
        }
    }

    private static List<string> PlainLines(IReadOnlyList<RichTextNode> nodes)
    {
        var lines = new List<string> { string.Empty };

        void Walk(IReadOnlyList<RichTextNode> children)
        {
            foreach (var node in children)
            {
                switch (node)
                {
                    case RichTextNode.Text text:
                        lines[^1] += text.Value;
                        break;
                    case RichTextNode.LineBreak:
                        lines.Add(string.Empty);
                        break;
                    case RichTextNode.Emphasis emphasis:
                        Walk(emphasis.Children);
                        break;
                }
            }
        }

        Walk(nodes);

        return lines;
    }

    // --- trimming -----------------------------------------------------------------------------

    /// <summary>
    /// Drops empty paragraphs at the two ends only. The editor emits a trailing
    /// <c>&lt;p&gt;&lt;br&gt;&lt;/p&gt;</c> almost every time; an empty paragraph <i>between</i> two
    /// filled ones is a blank line somebody typed on purpose, so it stays.
    /// </summary>
    private static List<RichTextNode> Trim(List<RichTextNode> blocks)
    {
        static bool IsBlank(RichTextNode node) =>
            node is RichTextNode.Paragraph { Children.Count: 0 };

        var start = 0;
        var end = blocks.Count;

        while (start < end && IsBlank(blocks[start]))
        {
            start++;
        }

        while (end > start && IsBlank(blocks[end - 1]))
        {
            end--;
        }

        return blocks.GetRange(start, end - start);
    }
}
