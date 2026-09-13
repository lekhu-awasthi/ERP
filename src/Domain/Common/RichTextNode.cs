namespace ErpApp.Domain.Common;

/// <summary>
/// Phase 39 -- the parsed form of a rich-text field, and the <i>only</i> shape any consumer sees.
///
/// <para>There are three consumers and they must agree: the stored HTML (re-emitted by
/// <see cref="RichText.Sanitize"/>), the printed PDF (<c>Api/Printing/RichTextPdfRenderer</c>), and
/// a plain-text flattening for spreadsheet cells and previews (<see cref="RichText.ToPlainText"/>).
/// Each of the three is a fold over this tree, so a tag the tree cannot represent is a tag no
/// consumer can disagree about.</para>
///
/// <para><b>Why the tree is this small.</b> Its member set is the intersection of what the reference
/// product's editor can produce (TinyMCE 7.1.1, toolbar read live on 2026-09-13 -- see
/// docs/phase-39-status.md) and what QuestPDF can be made to draw faithfully. Font family, font
/// size, text colour, indentation, line height, tables and images are all things that editor offers
/// and this tree deliberately does not carry; Decision B in the status doc states the divergence and
/// why a field that renders three different ways in three places is worse than one that renders one
/// way everywhere.</para>
/// </summary>
public abstract record RichTextNode
{
    private RichTextNode()
    {
    }

    /// <summary>A run of literal characters. Never contains markup: the parser has already decoded
    /// entities, and every emitter re-escapes on the way out.</summary>
    public sealed record Text(string Value) : RichTextNode;

    /// <summary>A line break inside a block.</summary>
    public sealed record LineBreak : RichTextNode;

    /// <summary>A horizontal rule between blocks.</summary>
    public sealed record Rule : RichTextNode;

    /// <summary>Bold / italic / underline / strikethrough, carrying inline children.</summary>
    public sealed record Emphasis(RichTextStyle Style, IReadOnlyList<RichTextNode> Children) : RichTextNode;

    /// <summary>One paragraph. <paramref name="Alignment"/> is re-derived, never copied -- see
    /// <see cref="RichText"/>.</summary>
    public sealed record Paragraph(RichTextAlignment Alignment, IReadOnlyList<RichTextNode> Children) : RichTextNode;

    /// <summary>A bulleted or numbered list.</summary>
    public sealed record List(bool Ordered, IReadOnlyList<ListItem> Items) : RichTextNode;

    /// <summary>One item of a <see cref="List"/>.</summary>
    public sealed record ListItem(IReadOnlyList<RichTextNode> Children) : RichTextNode;
}

/// <summary>The four character styles the editor's <c>fontstyle</c> button offers, observed live.</summary>
[Flags]
public enum RichTextStyle
{
    None = 0,
    Bold = 1,
    Italic = 2,
    Underline = 4,
    Strikethrough = 8,
}

/// <summary>
/// The only attribute value this codebase lets through a rich-text field -- and it does not let it
/// through, it re-derives it. A parser that recognised <c>text-align: left</c> would be filtering a
/// user-supplied string; this enum means the emitter writes one of four constants it owns, so no
/// byte of user input ever reaches an attribute position.
/// </summary>
public enum RichTextAlignment
{
    Start = 0,
    Center = 1,
    End = 2,
    Justify = 3,
}
