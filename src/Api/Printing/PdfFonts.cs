using QuestPDF.Drawing;

namespace ErpApp.Api.Printing;

/// <summary>
/// Phase 67 -- the fonts the PDF needs beyond QuestPDF's default. Its default font has no Devanagari,
/// so the Nepali heading (कर बीजक, क्रेडिट नोट) would print as missing glyphs. Noto Sans Devanagari is
/// bundled under <c>Printing/Fonts</c> with its SIL Open Font License, embedded in this assembly, and
/// registered once, on first use, by <see cref="EnsureRegistered"/>.
///
/// <para>Only the Nepali text names this family; everything else keeps the default font, so no
/// existing document's appearance changes.</para>
/// </summary>
public static class PdfFonts
{
    /// <summary>The family name inside the bundled TTFs.</summary>
    public const string Devanagari = "Noto Sans Devanagari";

    private static readonly Lazy<bool> Registered = new(Register, LazyThreadSafetyMode.ExecutionAndPublication);

    public static void EnsureRegistered() => _ = Registered.Value;

    private static bool Register()
    {
        var assembly = typeof(PdfFonts).Assembly;
        foreach (var name in new[] { "NotoSansDevanagari-Regular.ttf", "NotoSansDevanagari-Bold.ttf" })
        {
            using var stream = assembly.GetManifestResourceStream($"ErpApp.Api.Printing.Fonts.{name}")
                ?? throw new InvalidOperationException($"The embedded font {name} is missing from the Api assembly.");
            FontManager.RegisterFont(stream);
        }

        return true;
    }
}
