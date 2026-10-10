using ErpApp.Api.IntegrationTests.TestSupport;
using ErpApp.Api.Printing;
using ErpApp.Application.Printing.Queries.PrintDocument;
using ErpApp.Domain.Common;
using ErpApp.Domain.Configuration;

namespace ErpApp.Api.IntegrationTests;

/// <summary>
/// Phase 73 -- the three layouts and the template's elements asserted <b>in the PDF's text</b>, like phase 67's
/// tests: no host, database or Docker, a DTO in and <see cref="PdfText"/> reading the bytes back.
/// </summary>
public class Phase73PrintLayoutPdfTests
{
    static Phase73PrintLayoutPdfTests()
    {
        QuestPDF.Settings.License = QuestPDF.Infrastructure.LicenseType.Community;
        QuestPDF.Settings.CheckIfAllTextGlyphsAreAvailable = true;
    }

    private static PrintableDocumentDto Quotation(PrintLayout layout) =>
        new(
            DocumentType.Quotation,
            "Quotation",
            "QUO0007",
            "2026-10-09",
            Reference: "REF-9",
            OrganizationName: "Moonbeam Trading",
            OrganizationAddress: "Manbhawan",
            OrganizationPhone: "01-4000000",
            OrganizationEmail: null,
            OrganizationPan: "031564579",
            OrganizationWebsite: null,
            OrganizationLogo: null,
            PartyHeading: "Quotation For",
            PartyLabel: "C0001 — Adhi Kishan",
            PartyAddress: "KTM",
            PrintingTemplateName: "Plain",
            HeaderFields: [new PrintableFieldDto("Expiry Date", "2026-10-30")],
            Sections:
            [
                new PrintableSectionDto(
                    "Items",
                    [
                        new PrintableColumnDto("Product", 4),
                        new PrintableColumnDto("Qty", 1, AlignRight: true),
                        new PrintableColumnDto("Unit", 1, Element: PrintElement.Unit),
                        new PrintableColumnDto("Amount", 1, AlignRight: true),
                    ],
                    [new PrintableRowDto(["P-001 — Widget", "2", "Box", "1,000.00"])],
                    new PrintableRowDto(["Total", "2", "Box", "1,000.00"])),
                new PrintableSectionDto("Empty", [new PrintableColumnDto("Account", 4)], []),
            ],
            Summary: [new PrintableFieldDto("Grand Total", "1,000.00", Emphasise: true)],
            Notes: null,
            Terms: "<p>Valid for 21 days.</p>",
            CalendarNote: null)
        {
            Layout = layout,
            AmountInWords = "Rupees One Thousand Only",
            FooterText = "Bank: NIC Asia 0012345",
            CustomFields = [new PrintableFieldDto("Site", "Butwal depot"), new PrintableFieldDto("Buyer PO", "PO-77")],
        };

    [Theory]
    [InlineData(PrintLayout.Standard)]
    [InlineData(PrintLayout.Traditional)]
    [InlineData(PrintLayout.Minimal)]
    public void Every_layout_prints_the_whole_document_and_its_new_elements(PrintLayout layout)
    {
        var text = PdfText.Extract(DocumentPdfRenderer.Render(Quotation(layout)));

        foreach (var expected in new[]
                 {
                     "Moonbeam Trading", "PAN: 031564579", "QUOTATION", "QUO0007", "Ref: REF-9", "C0001 — Adhi Kishan",
                     "Expiry Date: 2026-10-30", "Site:", "Butwal depot", "Buyer PO:", "PO-77", "Box", "NPR 1,000.00",
                     "Amount in Words:", "Rupees One Thousand Only", "Valid for 21 days.", "Prepared By",
                     "Bank: NIC Asia 0012345",
                 })
        {
            Assert.Contains(expected, text, StringComparison.Ordinal);
        }

        // Phase 20d's proof-of-wiring line is gone now that the template visibly changes the page.
        Assert.DoesNotContain("Template:", text, StringComparison.Ordinal);
    }

    [Fact]
    public void A_hidden_signature_line_prints_no_signature_line()
    {
        var text = PdfText.Extract(DocumentPdfRenderer.Render(Quotation(PrintLayout.Standard) with { ShowSignatureLine = false }));

        Assert.DoesNotContain("Prepared By", text, StringComparison.Ordinal);
        Assert.DoesNotContain("Approved By", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Compact_margins_render_in_every_layout()
    {
        foreach (var layout in Enum.GetValues<PrintLayout>())
        {
            var text = PdfText.Extract(DocumentPdfRenderer.Render(Quotation(layout) with { CompactMargins = true }));
            Assert.Contains("QUO0007", text, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void Minimal_draws_no_logo_even_when_the_dto_carries_one()
    {
        // A one-pixel PNG: Standard draws it (an image XObject in the file), Minimal never does.
        byte[] png = Convert.FromBase64String(
            "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg==");

        var standard = System.Text.Encoding.Latin1.GetString(
            DocumentPdfRenderer.Render(Quotation(PrintLayout.Standard) with { OrganizationLogo = png }));
        var minimal = System.Text.Encoding.Latin1.GetString(
            DocumentPdfRenderer.Render(Quotation(PrintLayout.Minimal) with { OrganizationLogo = png }));

        Assert.Contains("/Subtype /Image", standard, StringComparison.Ordinal);
        Assert.DoesNotContain("/Subtype /Image", minimal, StringComparison.Ordinal);
    }
}
