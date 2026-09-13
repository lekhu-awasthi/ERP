namespace ErpApp.Domain.Common;

/// <summary>
/// Turns the tokenizer's generic tree into <see cref="RichTextNode"/>, which is where every "what
/// does this tag mean?" decision lives.
///
/// <para><b>Three verdicts, and the difference between two of them is the point.</b> A tag is
/// <i>recognised</i> (it becomes a node), <i>unwrapped</i> (it vanishes and its children are kept),
/// or <i>dropped with its subtree</i> -- and that last verdict was already applied by the tokenizer,
/// which never parses the content of a <c>&lt;script&gt;</c> at all. Unwrapping is the default,
/// because the failure it avoids is silent data loss: a paragraph pasted from Word arrives wrapped
/// in four <c>&lt;div&gt;</c>s and a <c>&lt;span&gt;</c>, and a stricter default would throw the
/// sentence away along with the wrappers.</para>
///
/// <para><b>What unwrapping costs, stated.</b> <c>&lt;a&gt;</c> keeps its text and loses its href;
/// <c>&lt;img&gt;</c> disappears; a table becomes one paragraph per row. Those are the four things
/// the reference product's editor offers that this tree cannot carry, and Decision B in
/// docs/phase-39-status.md is the argument for accepting that rather than teaching QuestPDF to draw
/// them.</para>
/// </summary>
internal static class Normalizer
{
    /// <summary>Tags that end the paragraph in progress and start a new one.</summary>
    private static readonly HashSet<string> Blocks = new(StringComparer.OrdinalIgnoreCase)
    {
        "p", "div", "blockquote", "pre", "section", "article", "header", "footer", "main", "aside",
        "figure", "figcaption", "address", "dl", "dd", "dt", "tr", "caption", "form", "fieldset",
        "h1", "h2", "h3", "h4", "h5", "h6",
    };

    /// <summary>Headings keep their weight but not their size -- a document's own typography owns
    /// scale, and a 36pt heading inside a printed invoice's Terms block is never what was meant.</summary>
    private static readonly HashSet<string> Headings = new(StringComparer.OrdinalIgnoreCase)
    {
        "h1", "h2", "h3", "h4", "h5", "h6",
    };

    private static readonly Dictionary<string, RichTextStyle> Emphases = new(StringComparer.OrdinalIgnoreCase)
    {
        ["b"] = RichTextStyle.Bold,
        ["strong"] = RichTextStyle.Bold,
        ["i"] = RichTextStyle.Italic,
        ["em"] = RichTextStyle.Italic,
        ["u"] = RichTextStyle.Underline,
        ["ins"] = RichTextStyle.Underline,
        ["s"] = RichTextStyle.Strikethrough,
        ["strike"] = RichTextStyle.Strikethrough,
        ["del"] = RichTextStyle.Strikethrough,
    };

    public static List<RichTextNode> ToBlocks(List<RawNode> roots)
    {
        var state = new BlockWriter();
        Walk(roots, state, RichTextAlignment.Start, RichTextStyle.None);
        state.FlushParagraph();

        return state.Blocks;
    }

    private static void Walk(
        List<RawNode> nodes, BlockWriter state, RichTextAlignment alignment, RichTextStyle style)
    {
        foreach (var node in nodes)
        {
            switch (node)
            {
                case RawNode.Text text:
                    state.Write(Styled(new RichTextNode.Text(text.Value), style));
                    break;

                case RawNode.Element element:
                    WalkElement(element, state, alignment, style);
                    break;
            }
        }
    }

    private static void WalkElement(
        RawNode.Element element, BlockWriter state, RichTextAlignment alignment, RichTextStyle style)
    {
        var name = element.Name;
        var inherited = element.Alignment ?? alignment;

        if (name.Equals("br", StringComparison.OrdinalIgnoreCase))
        {
            state.Write(new RichTextNode.LineBreak());

            return;
        }

        if (name.Equals("hr", StringComparison.OrdinalIgnoreCase))
        {
            state.FlushParagraph();
            state.Blocks.Add(new RichTextNode.Rule());

            return;
        }

        if (name.Equals("ul", StringComparison.OrdinalIgnoreCase)
            || name.Equals("ol", StringComparison.OrdinalIgnoreCase))
        {
            WalkList(element, state, inherited, style);

            return;
        }

        if (Emphases.TryGetValue(name, out var added))
        {
            // Styles compose rather than replace: bold inside italic is both, and the emitter nests
            // one tag per flag so the stored markup says the same.
            Walk(element.Children, state, inherited, style | added);

            return;
        }

        if (Blocks.Contains(name))
        {
            state.FlushParagraph();
            state.Alignment = inherited;
            var weight = Headings.Contains(name) ? style | RichTextStyle.Bold : style;
            Walk(element.Children, state, inherited, weight);
            state.FlushParagraph();
            state.Alignment = alignment;

            return;
        }

        if (name.Equals("td", StringComparison.OrdinalIgnoreCase)
            || name.Equals("th", StringComparison.OrdinalIgnoreCase))
        {
            // A cell is not a block -- its row is. Separating cells with a space is what keeps
            // "1,500" and "NPR" from being concatenated when a pasted table is flattened.
            Walk(element.Children, state, inherited, style);
            state.Write(Styled(new RichTextNode.Text(" "), style));

            return;
        }

        if (name.Equals("li", StringComparison.OrdinalIgnoreCase))
        {
            // A list item outside any list: treat it as a paragraph rather than dropping the text.
            state.FlushParagraph();
            Walk(element.Children, state, inherited, style);
            state.FlushParagraph();

            return;
        }

        // Everything else -- span, font, a, img, table, tbody, section wrappers, unknown tags --
        // unwraps. See the class comment for why that is the default.
        Walk(element.Children, state, inherited, style);
    }

    private static void WalkList(
        RawNode.Element element, BlockWriter state, RichTextAlignment alignment, RichTextStyle style)
    {
        state.FlushParagraph();

        var ordered = element.Name.Equals("ol", StringComparison.OrdinalIgnoreCase);
        var items = new List<RichTextNode.ListItem>();

        foreach (var child in element.Children)
        {
            if (child is not RawNode.Element { Name: var childName } item
                || !childName.Equals("li", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var inner = new BlockWriter { Alignment = item.Alignment ?? alignment };
            Walk(item.Children, inner, inner.Alignment, style);
            inner.FlushParagraph();

            // A nested list inside an item is flattened into the item's own text: the tree carries
            // one list level, because a printed terms block with three levels of indentation is not
            // a shape this app's one PDF layout has anywhere else.
            var children = inner.Blocks
                .OfType<RichTextNode.Paragraph>()
                .SelectMany((paragraph, index) => index == 0
                    ? paragraph.Children
                    : [new RichTextNode.LineBreak(), .. paragraph.Children])
                .ToList();

            items.Add(new RichTextNode.ListItem(children));
        }

        if (items.Count > 0)
        {
            state.Blocks.Add(new RichTextNode.List(ordered, items));
        }
    }

    /// <summary>Wraps one inline node in a tag per set style flag, outermost first.</summary>
    private static RichTextNode Styled(RichTextNode node, RichTextStyle style)
    {
        if (style.HasFlag(RichTextStyle.Strikethrough))
        {
            node = new RichTextNode.Emphasis(RichTextStyle.Strikethrough, [node]);
        }

        if (style.HasFlag(RichTextStyle.Underline))
        {
            node = new RichTextNode.Emphasis(RichTextStyle.Underline, [node]);
        }

        if (style.HasFlag(RichTextStyle.Italic))
        {
            node = new RichTextNode.Emphasis(RichTextStyle.Italic, [node]);
        }

        if (style.HasFlag(RichTextStyle.Bold))
        {
            node = new RichTextNode.Emphasis(RichTextStyle.Bold, [node]);
        }

        return node;
    }

    /// <summary>
    /// Accumulates inline content until something forces a paragraph boundary. A paragraph whose
    /// content is only whitespace is dropped: the editor emits those constantly and they would
    /// otherwise print as blank lines nobody asked for.
    /// </summary>
    private sealed class BlockWriter
    {
        public List<RichTextNode> Blocks { get; } = [];

        public RichTextAlignment Alignment { get; set; } = RichTextAlignment.Start;

        private readonly List<RichTextNode> pending = [];

        public void Write(RichTextNode node) => pending.Add(node);

        public void FlushParagraph()
        {
            if (pending.Count == 0)
            {
                return;
            }

            var children = HasVisibleText(pending) ? CollapseEdges(pending) : [];

            if (children.Count > 0)
            {
                Blocks.Add(new RichTextNode.Paragraph(Alignment, children));
            }
            else if (pending.Any(x => x is RichTextNode.LineBreak))
            {
                // A paragraph holding only breaks is a deliberate blank line. RichText.Trim drops
                // these at the two ends and keeps the ones between filled paragraphs.
                Blocks.Add(new RichTextNode.Paragraph(Alignment, []));
            }

            pending.Clear();
        }

        private static bool HasVisibleText(List<RichTextNode> nodes) => nodes.Any(Visible);

        private static bool Visible(RichTextNode node) => node switch
        {
            RichTextNode.Text text => text.Value.Trim().Length > 0,
            RichTextNode.Emphasis emphasis => emphasis.Children.Any(Visible),
            _ => false,
        };

        /// <summary>Trims the leading and trailing whitespace a block boundary makes meaningless,
        /// leaving the spaces between words alone.</summary>
        private static List<RichTextNode> CollapseEdges(List<RichTextNode> nodes)
        {
            var trimmed = new List<RichTextNode>(nodes);

            while (trimmed.Count > 0 && IsBlankText(trimmed[0]))
            {
                trimmed.RemoveAt(0);
            }

            while (trimmed.Count > 0 && IsBlankText(trimmed[^1]))
            {
                trimmed.RemoveAt(trimmed.Count - 1);
            }

            if (trimmed.Count > 0 && trimmed[0] is RichTextNode.Text first)
            {
                trimmed[0] = new RichTextNode.Text(first.Value.TrimStart());
            }

            if (trimmed.Count > 0 && trimmed[^1] is RichTextNode.Text last)
            {
                trimmed[^1] = new RichTextNode.Text(last.Value.TrimEnd());
            }

            return trimmed;
        }

        private static bool IsBlankText(RichTextNode node) =>
            node is RichTextNode.Text { Value: var value } && value.Trim().Length == 0;
    }
}
