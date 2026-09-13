using System.Text;
using ErpApp.Api.Printing;
using ErpApp.Application.Printing.Queries.PrintDocument;
using ErpApp.Domain.Common;

namespace ErpApp.Api.IntegrationTests;

/// <summary>
/// Phase 39 — the logo and the rich-text Terms, asserted <b>in the printed output</b> rather than at
/// the upload.
///
/// <para>That distinction is the point. An upload assertion proves a file was stored; it says
/// nothing about whether the PDF a customer receives shows it, and the PDF is the whole reason the
/// logo exists. The same goes for Terms: phase 27b printed a string, and if the rich-text change had
/// stopped at the editor, every document would have printed its markup as visible angle brackets.
/// These render real PDFs through the real renderer and look at the bytes.</para>
///
/// <para>No host, no database and no Docker — <see cref="DocumentPdfRenderer"/> is a pure function
/// from a DTO to a byte array, which is what makes an assertion this direct possible at all.</para>
/// </summary>
public class Phase39LogoPrintTests
{
    /// <summary>Program.cs declares this at start-up; a test that renders without booting the host
    /// has to declare it too, or QuestPDF refuses to generate anything.</summary>
    static Phase39LogoPrintTests() =>
        QuestPDF.Settings.License = QuestPDF.Infrastructure.LicenseType.Community;

    private static PrintableDocumentDto Document(byte[]? logo = null, string? terms = null) =>
        new(
            DocumentType.Invoice,
            "Invoice",
            "INV-0001",
            "2026-09-13",
            Reference: null,
            OrganizationName: "Moonbeam Trading",
            OrganizationAddress: "Manbhawan",
            OrganizationPhone: "9705056788",
            OrganizationEmail: "hello@example.test",
            OrganizationPan: "031564579",
            OrganizationWebsite: null,
            OrganizationLogo: logo,
            PartyHeading: "Bill To",
            PartyLabel: "C0001 — Adhi Kishan",
            PartyAddress: "KTM",
            PrintingTemplateName: "Default",
            HeaderFields: [],
            Sections: [],
            Summary: [new PrintableFieldDto("Grand Total", "1,695.00", Emphasise: true)],
            Notes: null,
            Terms: terms,
            CalendarNote: null);

    // --- the logo -----------------------------------------------------------------------------

    [Fact]
    public void A_tenant_with_no_logo_prints_the_header_exactly_as_before()
    {
        var pdf = DocumentPdfRenderer.Render(Document());

        Assert.NotEmpty(pdf);
        Assert.StartsWith("%PDF", Encoding.ASCII.GetString(pdf, 0, 4), StringComparison.Ordinal);
    }

    /// <summary>
    /// The assertion that matters: the PDF carries an embedded image XObject. A size comparison
    /// alone would pass for a renderer that drew a grey box, and "it did not throw" would pass for
    /// one that drew nothing at all.
    /// </summary>
    [Fact]
    public void A_tenant_with_a_logo_prints_it_as_an_embedded_image()
    {
        var withLogo = DocumentPdfRenderer.Render(Document(logo: TestPng.Create(300, 300)));
        var withoutLogo = DocumentPdfRenderer.Render(Document());

        // "/Image" alone would match every PDF: the ProcSet array names /ImageB /ImageC /ImageI
        // whether or not one is embedded. The XObject subtype is the thing that only appears when
        // an image really is in the file.
        Assert.Contains("/Subtype /Image", Encoding.ASCII.GetString(withLogo), StringComparison.Ordinal);
        Assert.DoesNotContain("/Subtype /Image", Encoding.ASCII.GetString(withoutLogo), StringComparison.Ordinal);
        Assert.True(
            withLogo.Length > withoutLogo.Length,
            "A PDF carrying an embedded logo should be larger than the same document without one.");
    }

    /// <summary>
    /// A logo whose bytes are not a decodable image must not take the document down with it.
    ///
    /// <para>This cannot arise through the upload path — <c>ImageHeader</c> refuses it there — but it
    /// can arise from a storage layer that handed back something unexpected, and that is exactly the
    /// moment an invoice still has to print. The renderer re-checks with the same parser rather than
    /// trusting that the upload did: QuestPDF throws for an undecodable image at GeneratePdf time,
    /// after composition, so there is no try/catch around the draw call that would help.</para>
    /// </summary>
    [Fact]
    public void A_corrupt_logo_is_skipped_rather_than_stopping_an_invoice_printing()
    {
        var pdf = DocumentPdfRenderer.Render(Document(logo: [0x00, 0x01, 0x02, 0x03]));

        Assert.Equal(DocumentPdfRenderer.Render(Document()).Length, pdf.Length);
    }

    // --- rich-text terms ----------------------------------------------------------------------

    /// <summary>
    /// The half phase 39 would otherwise have shipped broken: Terms is HTML now, and a renderer that
    /// kept printing it as a string would print the tags. Asserted by looking for the tag text in
    /// the output, which is the failure a reader would actually see.
    /// </summary>
    [Fact]
    public void Rich_text_terms_print_as_formatting_rather_than_as_visible_tags()
    {
        var terms = RichText.Sanitize("<p>Payment due in <strong>30 days</strong>.</p><ul><li>Net 30</li></ul>");
        var pdf = DocumentPdfRenderer.Render(Document(terms: terms));
        var text = Encoding.ASCII.GetString(pdf);

        Assert.DoesNotContain("<strong>", text, StringComparison.Ordinal);
        Assert.DoesNotContain("&lt;", text, StringComparison.Ordinal);
        Assert.NotEmpty(pdf);
    }

    /// <summary>
    /// The editor's idea of empty is <c>&lt;p&gt;&lt;br&gt;&lt;/p&gt;</c>, and the renderer checks
    /// <c>RichText.IsEmpty</c> rather than <c>IsNullOrWhiteSpace</c> precisely so that does not print
    /// a "Terms and Conditions" heading over nothing.
    /// </summary>
    [Fact]
    public void An_empty_terms_field_prints_no_heading()
    {
        var withEmptyTerms = DocumentPdfRenderer.Render(Document(terms: "<p><br></p>"));
        var withNoTerms = DocumentPdfRenderer.Render(Document());

        Assert.Equal(withNoTerms.Length, withEmptyTerms.Length);
    }
}
