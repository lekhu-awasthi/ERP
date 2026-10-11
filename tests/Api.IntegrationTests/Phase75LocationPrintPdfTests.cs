using ErpApp.Api.IntegrationTests.TestSupport;
using ErpApp.Api.Printing;
using ErpApp.Application.Printing.Queries.PrintDocument;
using ErpApp.Domain.Common;
using ErpApp.Domain.Configuration;

namespace ErpApp.Api.IntegrationTests;

/// <summary>
/// Phase 75 -- the document's location on its own line under the organization's address, in every layout, asserted in
/// the PDF's text as phase 73's tests are.
/// </summary>
public class Phase75LocationPrintPdfTests
{
    static Phase75LocationPrintPdfTests()
    {
        QuestPDF.Settings.License = QuestPDF.Infrastructure.LicenseType.Community;
        QuestPDF.Settings.CheckIfAllTextGlyphsAreAvailable = true;
    }

    private static PrintableDocumentDto Invoice(PrintLayout layout, string? locationName, string? locationAddress) =>
        new(
            DocumentType.Invoice,
            "Tax Invoice",
            "INV0003/1002",
            "2083-06-24 BS (2026-10-10 AD)",
            Reference: null,
            OrganizationName: "Moonbeam Trading",
            OrganizationAddress: "Manbhawan, Lalitpur",
            OrganizationPhone: null,
            OrganizationEmail: null,
            OrganizationPan: "031564579",
            OrganizationWebsite: null,
            OrganizationLogo: null,
            PartyHeading: "Bill To",
            PartyLabel: "C0001 — Adhi Kishan",
            PartyAddress: "KTM",
            PrintingTemplateName: "Plain",
            HeaderFields: [],
            Sections:
            [
                new PrintableSectionDto(
                    "Items",
                    [new PrintableColumnDto("Product", 4), new PrintableColumnDto("Amount", 1, AlignRight: true)],
                    [new PrintableRowDto(["P-001 — Widget", "1,000.00"])]),
            ],
            Summary: [new PrintableFieldDto("Grand Total", "1,130.00", Emphasise: true)],
            Notes: null,
            Terms: null,
            CalendarNote: null)
        {
            Layout = layout,
            LocationName = locationName,
            LocationAddress = locationAddress,
        };

    [Theory]
    [InlineData(PrintLayout.Standard)]
    [InlineData(PrintLayout.Traditional)]
    [InlineData(PrintLayout.Minimal)]
    [InlineData(PrintLayout.Modern)]
    [InlineData(PrintLayout.Classic)]
    [InlineData(PrintLayout.Retail)]
    public void Every_layout_prints_the_location_under_the_registered_address(PrintLayout layout)
    {
        var text = PdfText.Extract(DocumentPdfRenderer.Render(Invoice(layout, "Lakeside", "Pokhara-6")));

        Assert.Contains("Manbhawan, Lalitpur", text, StringComparison.Ordinal);
        Assert.Contains("Location: Lakeside, Pokhara-6", text, StringComparison.Ordinal);
        Assert.True(
            text.IndexOf("Manbhawan, Lalitpur", StringComparison.Ordinal) < text.IndexOf("Location:", StringComparison.Ordinal),
            "The location line follows the organization's address.");
    }

    [Fact]
    public void A_location_without_an_address_prints_its_name_alone()
    {
        var text = PdfText.Extract(DocumentPdfRenderer.Render(Invoice(PrintLayout.Standard, "HeadOffice", null)));

        Assert.Contains("Location: HeadOffice", text, StringComparison.Ordinal);
        Assert.DoesNotContain("HeadOffice,", text, StringComparison.Ordinal);
    }

    [Fact]
    public void No_location_prints_no_line()
    {
        var text = PdfText.Extract(DocumentPdfRenderer.Render(Invoice(PrintLayout.Standard, null, null)));

        Assert.DoesNotContain("Location:", text, StringComparison.Ordinal);
    }
}
