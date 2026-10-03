using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;

namespace ErpApp.Api.IntegrationTests.TestSupport;

/// <summary>
/// Phase 67 -- the text a PDF shows, recovered well enough to assert on. A rendered PDF's content is
/// deflated and its glyphs are font-internal ids, so a byte search finds neither "COPY OF ORIGINAL" nor
/// "कर बीजक". This inflates every stream and maps each shown glyph back through the font's own
/// <c>/ToUnicode</c> table, which is what a PDF viewer's copy-and-paste does.
///
/// <para>Deliberately small: it reads the shapes QuestPDF writes (Type0 fonts, hex <c>Tj</c>/<c>TJ</c>
/// strings) and nothing else. Text runs are joined with a space, so assert with <c>Contains</c> on
/// short phrases, not on layout. Devanagari comes back in the font's glyph order, which for a conjunct
/// can differ from the typed order; assert on the plain-letter headings.</para>
/// </summary>
internal static partial class PdfText
{
    public static string Extract(byte[] pdf)
    {
        var raw = Encoding.Latin1.GetString(pdf);

        var objects = new Dictionary<string, (string Dictionary, string? Stream)>();
        foreach (Match match in ObjectPattern().Matches(raw))
        {
            var body = match.Groups[2].Value;
            var streamAt = body.IndexOf("stream", StringComparison.Ordinal);
            if (streamAt < 0)
            {
                objects[match.Groups[1].Value] = (body, null);
                continue;
            }

            var dictionary = body[..streamAt];
            var start = streamAt + "stream".Length;
            if (body[start] == '\r') start++;
            if (body[start] == '\n') start++;
            var end = body.LastIndexOf("endstream", StringComparison.Ordinal);
            var bytes = Encoding.Latin1.GetBytes(body[start..end]);

            objects[match.Groups[1].Value] = (dictionary, dictionary.Contains("/FlateDecode", StringComparison.Ordinal)
                ? Inflate(bytes)
                : Encoding.Latin1.GetString(bytes));
        }

        // Font object id -> glyph id -> text.
        var unicodeMaps = new Dictionary<string, Dictionary<int, string>>();
        foreach (var (id, (dictionary, _)) in objects)
        {
            var toUnicode = ToUnicodePattern().Match(dictionary);
            if (toUnicode.Success && objects.TryGetValue(toUnicode.Groups[1].Value, out var cmap) && cmap.Stream is not null)
            {
                unicodeMaps[id] = ParseCMap(cmap.Stream);
            }
        }

        // Resource name (/F1) -> font object id. Names are per page, but QuestPDF numbers them
        // uniquely enough for a one-document assertion.
        var fontByName = new Dictionary<string, string>();
        foreach (var (_, (dictionary, _)) in objects)
        {
            foreach (Match font in FontResourcePattern().Matches(dictionary))
            {
                fontByName[font.Groups[1].Value] = font.Groups[2].Value;
            }
        }

        var text = new StringBuilder();
        foreach (var (_, (_, stream)) in objects)
        {
            if (stream is null || !stream.Contains(" Tf", StringComparison.Ordinal))
            {
                continue;
            }

            Dictionary<int, string>? map = null;
            foreach (Match token in ContentPattern().Matches(stream))
            {
                if (token.Groups["font"].Success)
                {
                    map = fontByName.TryGetValue(token.Groups["font"].Value, out var fontId)
                        ? unicodeMaps.GetValueOrDefault(fontId)
                        : null;
                    continue;
                }

                if (token.Value == "BT")
                {
                    text.Append(' ');
                    continue;
                }

                var hexes = token.Groups["hex"].Success
                    ? [token.Groups["hex"].Value]
                    : HexPattern().Matches(token.Groups["array"].Value).Select(x => x.Groups[1].Value);
                foreach (var hex in hexes)
                {
                    for (var i = 0; i + 4 <= hex.Length; i += 4)
                    {
                        var glyph = int.Parse(hex.AsSpan(i, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
                        text.Append(map is not null && map.TryGetValue(glyph, out var s) ? s : "?");
                    }
                }
            }
        }

        return text.ToString();
    }

    private static string Inflate(byte[] bytes)
    {
        using var input = new MemoryStream(bytes);
        using var zlib = new ZLibStream(input, CompressionMode.Decompress);
        using var output = new MemoryStream();
        zlib.CopyTo(output);
        return Encoding.Latin1.GetString(output.ToArray());
    }

    private static Dictionary<int, string> ParseCMap(string cmap)
    {
        var map = new Dictionary<int, string>();

        // Each pattern is read only inside its own section: a range pattern let loose on a bfchar
        // block reads two consecutive one-glyph lines as one range and garbles the map.
        foreach (Match section in SectionPattern("bfrange").Matches(cmap))
        {
            var body = section.Groups[1].Value;
            foreach (Match range in RangeArrayPattern().Matches(body))
            {
                var glyph = Hex(range.Groups[1].Value);
                foreach (Match target in HexPattern().Matches(range.Groups[3].Value))
                {
                    map[glyph++] = Utf16(target.Groups[1].Value);
                }
            }

            foreach (Match range in RangePattern().Matches(body))
            {
                var from = Hex(range.Groups[1].Value);
                var to = Hex(range.Groups[2].Value);
                var first = Hex(range.Groups[3].Value);
                for (var glyph = from; glyph <= to; glyph++)
                {
                    map.TryAdd(glyph, char.ConvertFromUtf32(first + glyph - from));
                }
            }
        }

        foreach (Match section in SectionPattern("bfchar").Matches(cmap))
        {
            foreach (Match pair in CharPattern().Matches(section.Groups[1].Value))
            {
                map[Hex(pair.Groups[1].Value)] = Utf16(pair.Groups[2].Value);
            }
        }

        return map;
    }

    private static Regex SectionPattern(string kind) =>
        new($@"begin{kind}([\s\S]*?)end{kind}", RegexOptions.None, TimeSpan.FromSeconds(1));

    private static int Hex(string value) => int.Parse(value, NumberStyles.HexNumber, CultureInfo.InvariantCulture);

    /// <summary>A ToUnicode target is UTF-16BE, and one glyph may stand for several characters.</summary>
    private static string Utf16(string hex)
    {
        var bytes = Convert.FromHexString(hex);
        return Encoding.BigEndianUnicode.GetString(bytes);
    }

    [GeneratedRegex(@"(\d+) 0 obj([\s\S]*?)endobj")]
    private static partial Regex ObjectPattern();

    [GeneratedRegex(@"/ToUnicode (\d+) 0 R")]
    private static partial Regex ToUnicodePattern();

    [GeneratedRegex(@"/(F\w*) (\d+) 0 R")]
    private static partial Regex FontResourcePattern();

    [GeneratedRegex(@"/(?<font>F\w*) [\d.]+ Tf|<(?<hex>[0-9A-Fa-f]+)> ?Tj|\[(?<array>[^\]]*)\] ?TJ|\bBT\b")]
    private static partial Regex ContentPattern();

    [GeneratedRegex(@"<([0-9A-Fa-f]+)>")]
    private static partial Regex HexPattern();

    [GeneratedRegex(@"<([0-9A-Fa-f]+)>\s*<([0-9A-Fa-f]+)>\s*\[([^\]]*)\]")]
    private static partial Regex RangeArrayPattern();

    [GeneratedRegex(@"<([0-9A-Fa-f]+)>\s*<([0-9A-Fa-f]+)>\s*<([0-9A-Fa-f]+)>")]
    private static partial Regex RangePattern();

    [GeneratedRegex(@"^<([0-9A-Fa-f]+)>\s*<([0-9A-Fa-f]+)>\s*$", RegexOptions.Multiline)]
    private static partial Regex CharPattern();
}
