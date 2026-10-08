using ErpApp.Api.IntegrationTests.TestSupport;
using ErpApp.Api.Printing;
using ErpApp.Application.Printing.Queries.PrintDocument;
using ErpApp.Domain.Common;

namespace ErpApp.Api.IntegrationTests;

/// <summary>
/// The buyer's PAN in the customer block of the ERP invoice and credit-note PDF (VAT Rules Schedule 5),
/// asserted in the PDF's text as phase 67's tests are. Printed only when the contact has one; a buyer
/// without a PAN prints exactly as before. Needs no host, database or Docker.
/// </summary>
public class BuyerPanPrintPdfTests
{
    static BuyerPanPrintPdfTests()
    {
        QuestPDF.Settings.License = QuestPDF.Infrastructure.LicenseType.Community;
        QuestPDF.Settings.CheckIfAllTextGlyphsAreAvailable = true;
    }

    private static PrintableDocumentDto Document(DocumentType type, string title, string? partyPan) =>
        new(
            type,
            title,
            "INV0001",
            "2026-10-03",
            Reference: null,
            OrganizationName: "Moonbeam Trading",
            OrganizationAddress: "Manbhawan",
            OrganizationPhone: null,
            OrganizationEmail: null,
            OrganizationPan: "031564579",
            OrganizationWebsite: null,
            OrganizationLogo: null,
            PartyHeading: type == DocumentType.CreditNote ? "Credit To" : "Bill To",
            PartyLabel: "C0001 — Adhi Kishan",
            PartyAddress: "KTM",
            PrintingTemplateName: "Default",
            HeaderFields: [],
            Sections: [],
            Summary: [new PrintableFieldDto("Grand Total", "1,695.00", Emphasise: true)],
            Notes: null,
            Terms: null,
            CalendarNote: null,
            PrintedCopy: new PrintedCopyDto(1, "Asha Accountant", "2026-10-03 09:15"),
            PartyPan: partyPan);

    [Theory]
    [InlineData(DocumentType.Invoice, "Tax Invoice")]
    [InlineData(DocumentType.CreditNote, "Credit Note")]
    public void A_buyer_with_a_pan_has_it_printed_in_the_customer_block(DocumentType type, string title)
    {
        var text = PdfText.Extract(DocumentPdfRenderer.Render(Document(type, title, "609876543")));

        Assert.Contains("PAN: 609876543", text, StringComparison.Ordinal);
        // The seller's own PAN still prints in the organization block, so the buyer's is a second one.
        Assert.Equal(2, PanLines(text));
    }

    private static int PanLines(string text) => text.Split("PAN: ", StringSplitOptions.None).Length - 1;

    [Theory]
    [InlineData(DocumentType.Invoice, "Tax Invoice")]
    [InlineData(DocumentType.CreditNote, "Credit Note")]
    public void A_buyer_without_a_pan_prints_no_pan_line(DocumentType type, string title)
    {
        var text = PdfText.Extract(DocumentPdfRenderer.Render(Document(type, title, partyPan: null)));

        Assert.Contains("Adhi Kishan", text, StringComparison.Ordinal);
        // Only the seller's "PAN: 031564579" in the organization block.
        Assert.Equal(1, PanLines(text));
        Assert.Contains("PAN: 031564579", text, StringComparison.Ordinal);
    }
}
