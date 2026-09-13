using System.Globalization;
using System.Text;

namespace ErpApp.Domain.Common;

/// <summary>
/// A generic element/text tree, the intermediate form between the tokenizer and
/// <see cref="RichTextNode"/>. Two small passes rather than one clever one: the tokenizer worries
/// only about malformed markup, the normalizer only about which tags mean what.
/// </summary>
internal abstract record RawNode
{
    private RawNode()
    {
    }

    public sealed record Text(string Value) : RawNode;

    /// <param name="Alignment">Read from <c>style="text-align:…"</c> or the legacy <c>align</c>
    /// attribute, and immediately reduced to an enum. No other attribute is retained at all -- see
    /// <see cref="RichText"/> for why that is the whole security argument.</param>
    public sealed record Element(string Name, RichTextAlignment? Alignment, List<RawNode> Children) : RawNode;
}

/// <summary>
/// A forgiving HTML tokenizer. It has to be forgiving rather than strict, because its input is
/// whatever a WYSIWYG editor produced plus whatever a user typed into a Source-code box, and
/// refusing to store a document because its markup is malformed would be a worse failure than
/// rendering it approximately.
///
/// <para>Being forgiving is safe here only because of what happens downstream: nothing this class
/// returns is ever echoed. Tag names are matched against fixed sets and then discarded; attribute
/// values are parsed solely to be skipped, except for the one that is reduced to a four-valued enum.
/// </para>
/// </summary>
internal static class RawParser
{
    /// <summary>Elements with no end tag. <c>li</c>/<c>p</c> are <i>not</i> here -- they are handled
    /// as implicitly-closed instead, which is what browsers do and what TinyMCE sometimes emits.</summary>
    private static readonly HashSet<string> Void = new(StringComparer.OrdinalIgnoreCase)
    {
        "area", "base", "br", "col", "embed", "hr", "img", "input", "link", "meta", "param",
        "source", "track", "wbr",
    };

    /// <summary>
    /// Elements whose <i>content</i> is not markup and must be discarded with them. The
    /// distinction matters: an unwrapped <c>&lt;span&gt;</c> should keep its text, and a dropped
    /// <c>&lt;script&gt;</c> must not.
    /// </summary>
    private static readonly HashSet<string> Opaque = new(StringComparer.OrdinalIgnoreCase)
    {
        "script", "style", "iframe", "object", "embed", "svg", "math", "noscript", "template",
        "textarea", "title", "head", "frame", "frameset", "applet",
    };

    /// <summary>Elements that implicitly close an open element of the same kind.</summary>
    private static readonly HashSet<string> ClosedByPeer = new(StringComparer.OrdinalIgnoreCase)
    {
        "p", "li", "td", "th", "tr", "dd", "dt",
    };

    public static List<RawNode> Parse(string html)
    {
        var roots = new List<RawNode>();
        var open = new Stack<RawNode.Element>();
        var text = new StringBuilder();
        var i = 0;

        List<RawNode> Current() => open.Count > 0 ? open.Peek().Children : roots;

        void FlushText()
        {
            if (text.Length > 0)
            {
                Current().Add(new RawNode.Text(text.ToString()));
                text.Clear();
            }
        }

        while (i < html.Length)
        {
            var c = html[i];

            if (c != '<')
            {
                if (c == '&')
                {
                    text.Append(ReadEntity(html, ref i));
                    continue;
                }

                // Every run of whitespace in HTML collapses to a single space -- not one space per
                // character. The difference is invisible until somebody indents their markup, at
                // which point an un-collapsed version prints a paragraph pushed halfway across the
                // page. The client's DOMParser half does this natively, and rich-text-cases.json is
                // what caught the two halves disagreeing about it.
                if (char.IsWhiteSpace(c))
                {
                    if (text.Length == 0 || text[^1] != ' ')
                    {
                        text.Append(' ');
                    }

                    i++;
                    continue;
                }

                text.Append(c);
                i++;
                continue;
            }

            if (html.AsSpan(i).StartsWith("<!--", StringComparison.Ordinal))
            {
                var close = html.IndexOf("-->", i + 4, StringComparison.Ordinal);
                i = close < 0 ? html.Length : close + 3;
                continue;
            }

            if (i + 1 < html.Length && (html[i + 1] == '!' || html[i + 1] == '?'))
            {
                var close = html.IndexOf('>', i);
                i = close < 0 ? html.Length : close + 1;
                continue;
            }

            var isEnd = i + 1 < html.Length && html[i + 1] == '/';
            var nameStart = i + (isEnd ? 2 : 1);

            if (nameStart >= html.Length || !char.IsAsciiLetter(html[nameStart]))
            {
                // A bare "<" that starts no tag is literal text -- and the escape on the way out is
                // what makes that safe to say.
                text.Append('<');
                i++;
                continue;
            }

            var nameEnd = nameStart;
            while (nameEnd < html.Length && (char.IsAsciiLetterOrDigit(html[nameEnd]) || html[nameEnd] == '-'))
            {
                nameEnd++;
            }

            var name = html[nameStart..nameEnd];

            FlushText();

            if (isEnd)
            {
                i = SkipToTagEnd(html, nameEnd);
                CloseElement(open, name);
                continue;
            }

            var alignment = ReadAttributes(html, nameEnd, out var selfClosing, out i);

            if (Opaque.Contains(name))
            {
                i = SkipOpaque(html, name, i);
                continue;
            }

            var element = new RawNode.Element(name, alignment, []);

            if (ClosedByPeer.Contains(name) && open.Count > 0
                && string.Equals(open.Peek().Name, name, StringComparison.OrdinalIgnoreCase))
            {
                open.Pop();
            }

            Current().Add(element);

            if (!selfClosing && !Void.Contains(name))
            {
                open.Push(element);
            }
        }

        FlushText();

        return roots;
    }

    /// <summary>
    /// Pops to the matching open element if there is one, and otherwise ignores the end tag
    /// entirely. Ignoring is the important half: a stray <c>&lt;/div&gt;</c> must not unwind
    /// everything above it, or one typo in a Source-code edit would reparent the whole document.
    /// </summary>
    private static void CloseElement(Stack<RawNode.Element> open, string name)
    {
        if (!open.Any(x => string.Equals(x.Name, name, StringComparison.OrdinalIgnoreCase)))
        {
            return;
        }

        while (open.Count > 0)
        {
            if (string.Equals(open.Pop().Name, name, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }
        }
    }

    /// <summary>
    /// Walks the attribute list, honouring quoting so that a <c>&gt;</c> inside a value cannot end
    /// the tag early, and returns the only thing kept: the declared alignment.
    /// </summary>
    private static RichTextAlignment? ReadAttributes(string html, int from, out bool selfClosing, out int next)
    {
        RichTextAlignment? alignment = null;
        selfClosing = false;
        var i = from;

        while (i < html.Length)
        {
            while (i < html.Length && char.IsWhiteSpace(html[i]))
            {
                i++;
            }

            if (i >= html.Length)
            {
                break;
            }

            if (html[i] == '>')
            {
                i++;
                break;
            }

            if (html[i] == '/')
            {
                selfClosing = true;
                i++;
                continue;
            }

            var nameStart = i;
            while (i < html.Length && html[i] != '=' && html[i] != '>' && !char.IsWhiteSpace(html[i]))
            {
                i++;
            }

            var attribute = html[nameStart..i];

            while (i < html.Length && char.IsWhiteSpace(html[i]))
            {
                i++;
            }

            var value = string.Empty;

            if (i < html.Length && html[i] == '=')
            {
                i++;
                while (i < html.Length && char.IsWhiteSpace(html[i]))
                {
                    i++;
                }

                if (i < html.Length && (html[i] == '"' || html[i] == '\''))
                {
                    var quote = html[i++];
                    var valueStart = i;
                    while (i < html.Length && html[i] != quote)
                    {
                        i++;
                    }

                    value = html[valueStart..i];
                    if (i < html.Length)
                    {
                        i++;
                    }
                }
                else
                {
                    var valueStart = i;
                    while (i < html.Length && html[i] != '>' && !char.IsWhiteSpace(html[i]))
                    {
                        i++;
                    }

                    value = html[valueStart..i];
                }
            }

            alignment ??= ReadAlignment(attribute, value);
        }

        next = i;

        return alignment;
    }

    private static RichTextAlignment? ReadAlignment(string attribute, string value)
    {
        string? keyword = null;

        if (attribute.Equals("align", StringComparison.OrdinalIgnoreCase))
        {
            keyword = value.Trim();
        }
        else if (attribute.Equals("style", StringComparison.OrdinalIgnoreCase))
        {
            foreach (var declaration in value.Split(';'))
            {
                var parts = declaration.Split(':', 2);
                if (parts.Length == 2 && parts[0].Trim().Equals("text-align", StringComparison.OrdinalIgnoreCase))
                {
                    keyword = parts[1].Trim();
                    break;
                }
            }
        }

        if (keyword is null)
        {
            return null;
        }

        // The value is matched against a closed set and then thrown away: what reaches the output is
        // whichever enum member matched, never the string that matched it.
        return keyword.ToLowerInvariant() switch
        {
            "center" => RichTextAlignment.Center,
            "right" or "end" => RichTextAlignment.End,
            "justify" => RichTextAlignment.Justify,
            "left" or "start" => RichTextAlignment.Start,
            _ => null,
        };
    }

    private static int SkipToTagEnd(string html, int from)
    {
        var close = html.IndexOf('>', from);

        return close < 0 ? html.Length : close + 1;
    }

    /// <summary>Skips to just past this element's end tag, discarding its content unparsed.</summary>
    private static int SkipOpaque(string html, string name, int from)
    {
        var needle = "</" + name;
        var close = html.IndexOf(needle, from, StringComparison.OrdinalIgnoreCase);

        return close < 0 ? html.Length : SkipToTagEnd(html, close);
    }

    /// <summary>
    /// Decodes one entity, or returns the literal <c>&amp;</c> when it is not one. Decoding matters
    /// for round-trip stability rather than for display: without it, a stored <c>&amp;amp;</c> would
    /// be re-escaped to <c>&amp;amp;amp;</c> on the next save, and a field would visibly rot over a
    /// handful of edits.
    /// </summary>
    private static string ReadEntity(string html, ref int i)
    {
        var semicolon = html.IndexOf(';', i + 1);

        if (semicolon > i + 1 && semicolon - i <= 12)
        {
            var body = html[(i + 1)..semicolon];
            string? decoded = null;

            if (body.StartsWith('#'))
            {
                var digits = body[1..];
                var hex = digits.StartsWith('x') || digits.StartsWith('X');
                var parsed = hex
                    ? int.TryParse(digits[1..], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var h) ? h : -1
                    : int.TryParse(digits, NumberStyles.Integer, CultureInfo.InvariantCulture, out var d) ? d : -1;

                if (parsed is > 0 and <= 0x10FFFF && (parsed < 0xD800 || parsed > 0xDFFF))
                {
                    decoded = char.ConvertFromUtf32(parsed);
                }
            }
            else
            {
                decoded = body.ToLowerInvariant() switch
                {
                    "amp" => "&",
                    "lt" => "<",
                    "gt" => ">",
                    "quot" => "\"",
                    "apos" or "#39" => "'",
                    "nbsp" => " ",
                    _ => null,
                };
            }

            if (decoded is not null)
            {
                i = semicolon + 1;

                return decoded;
            }
        }

        i++;

        return "&";
    }
}
