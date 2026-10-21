using System.Globalization;
using System.Text.RegularExpressions;
using ErpApp.Api.IntegrationTests.TestSupport;
using ErpApp.Api.Printing;
using ErpApp.Application.Printing.Queries.PrintDocument;
using ErpApp.Domain.Common;
using ErpApp.Domain.Configuration;

namespace ErpApp.Api.IntegrationTests;

/// <summary>
/// Phase 79 -- Modern, Classic and the 80 mm roll, asserted in the PDF's text as phase 73's layouts are: a tax invoice
/// carrying everything Schedule 5 asks for must still carry all of it in each new layout, which is the property the
/// vendor's own Retail layout does not have (it prints no transaction date).
/// </summary>
public class Phase79PrintLayoutPdfTests
{
    static Phase79PrintLayoutPdfTests()
    {
        QuestPDF.Settings.License = QuestPDF.Infrastructure.LicenseType.Community;
        QuestPDF.Settings.CheckIfAllTextGlyphsAreAvailable = true;
    }

    /// <summary>A one-pixel PNG.</summary>
    private static readonly byte[] Png = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg==");

    private static PrintableDocumentDto Invoice(PrintLayout layout) =>
        new(
            DocumentType.Invoice,
            "Tax Invoice",
            "INV0003/1002",
            "2083-06-24 BS (2026-10-10 AD)",
            Reference: "PO-55",
            OrganizationName: "Moonbeam Trading",
            OrganizationAddress: "Manbhawan, Lalitpur",
            OrganizationPhone: "01-4000000",
            OrganizationEmail: null,
            OrganizationPan: "031564579",
            OrganizationWebsite: null,
            OrganizationLogo: null,
            PartyHeading: "Bill To",
            PartyLabel: "C0001 — Adhi Kishan",
            PartyAddress: "Kalimati, KTM",
            PrintingTemplateName: "Plain",
            HeaderFields: [new PrintableFieldDto("Due Date", "2083-07-10 BS (2026-10-27 AD)")],
            Sections:
            [
                new PrintableSectionDto(
                    "Items",
                    [
                        new PrintableColumnDto("Product", 3.4f),
                        new PrintableColumnDto("Qty", 0.8f, AlignRight: true),
                        new PrintableColumnDto("Unit", 0.8f, Element: PrintElement.Unit),
                        new PrintableColumnDto("Rate", 1.4f, AlignRight: true),
                        new PrintableColumnDto("Disc %", 0.8f, AlignRight: true, Element: PrintElement.DiscountColumn),
                        new PrintableColumnDto("VAT", 1.2f, AlignRight: true, Element: PrintElement.VatColumn),
                        new PrintableColumnDto("Amount", 1.5f, AlignRight: true),
                    ],
                    [
                        new PrintableRowDto(["P-001 — Widget", "2", "Box", "1,000.00", "10", "234.00", "2,034.00"]),
                        new PrintableRowDto(["P-002 — Exempt rice", "5", "Kg", "100.00", "0", "0.00", "500.00"]),
                    ],
                    new PrintableRowDto(["Total", "", "", "", "", "234.00", "2,534.00"])),
            ],
            Summary:
            [
                new PrintableFieldDto("Sub Total", "2,300.00"),
                new PrintableFieldDto("VAT", "234.00"),
                new PrintableFieldDto("Grand Total", "2,534.00", Emphasise: true),
            ],
            Notes: null,
            Terms: "<p>Goods once sold are not returned.</p>",
            CalendarNote: null,
            TitleNepali: "कर बीजक",
            PrintedCopy: new PrintedCopyDto(3, "Asha", "2083-06-24 14:05"),
            PartyPan: "609876543",
            DateLabel: "Transaction Date",
            IssueDateText: "2083-06-25 BS (2026-10-11 AD)")
        {
            Layout = layout,
            AmountInWords = "Rupees Two Thousand Five Hundred Thirty Four Only",
            FooterText = "Thank you for shopping with us",
            CustomFields = [new PrintableFieldDto("Vehicle", "Ba 2 Kha 1234")],
        };

    private static string Text(PrintableDocumentDto dto) => PdfText.Extract(DocumentPdfRenderer.Render(dto));

    [Theory]
    [InlineData(PrintLayout.Modern)]
    [InlineData(PrintLayout.Classic)]
    [InlineData(PrintLayout.Retail)]
    public void Every_new_layout_prints_everything_a_tax_invoice_must_carry(PrintLayout layout)
    {
        var text = Text(Invoice(layout));

        foreach (var expected in new[]
                 {
                     // The seller (Schedule 5): name, address, PAN.
                     "Moonbeam Trading", "Manbhawan, Lalitpur", "PAN: 031564579",
                     // The bill: its title, number and both statutory dates.
                     "TAX INVOICE", "INV0003/1002", "Transaction Date", "2083-06-24 BS (2026-10-10 AD)",
                     "Issue Date", "2083-06-25 BS (2026-10-11 AD)", "PO-55", "Due Date",
                     // The buyer: name, address, PAN.
                     "C0001 — Adhi Kishan", "Kalimati, KTM", "PAN: 609876543",
                     // The lines and the totals.
                     "P-001 — Widget", "P-002 — Exempt rice", "Box", "2,034.00", "500.00", "Sub Total", "2,300.00",
                     "NPR 2,534.00", "Rupees Two Thousand Five Hundred Thirty Four Only",
                     // The rest of the template and the 2072 procedure's print record.
                     "Vehicle", "Ba 2 Kha 1234", "Goods once sold are not returned.", "Thank you for shopping with us",
                     "COPY OF ORIGINAL", "printed 3 times", "Printed by Asha on 2083-06-24 14:05",
                 })
        {
            Assert.Contains(expected, text, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void The_roll_is_one_page_eighty_millimetres_wide()
    {
        var pdf = System.Text.Encoding.Latin1.GetString(DocumentPdfRenderer.Render(Invoice(PrintLayout.Retail)));

        var widths = Regex.Matches(pdf, @"/MediaBox\s*\[\s*0\s+0\s+([0-9.]+)\s+([0-9.]+)\s*\]")
            .Select(m => float.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture))
            .ToList();

        // One page, 80 mm = 226.77 pt.
        Assert.Single(widths);
        Assert.InRange(widths[0], 226f, 227.5f);
    }

    [Fact]
    public void The_roll_stacks_each_line_and_keeps_its_vat_and_discount()
    {
        var text = Text(Invoice(PrintLayout.Retail));

        // The vendor's Retail drops the discount and VAT columns; the roll keeps both as the line's small print, so
        // its Amount can be read back from what is on the paper.
        Assert.Contains("Disc %", text, StringComparison.Ordinal);
        Assert.Contains("234.00", text, StringComparison.Ordinal);
        Assert.True(
            text.IndexOf("P-001 — Widget", StringComparison.Ordinal) < text.IndexOf("Qty", StringComparison.Ordinal),
            "A line's name prints above its figures.");
    }

    /// <summary>Phase 98 -- the money receipt (a Payment, the shape <c>PrintDocumentQueryHandler.BuildPaymentAsync</c>
    /// builds) on the 80 mm roll: one page that wide, its date in BS with AD beside it, who paid, the account, every
    /// document it settles, what is left unallocated and the amount in words.</summary>
    [Fact]
    public void A_money_receipt_prints_whole_on_the_roll()
    {
        var receipt = Invoice(PrintLayout.Retail) with
        {
            DocumentType = DocumentType.Payment,
            Title = "Customer Receipt",
            TitleNepali = null,
            Code = "RCPT0007",
            DateText = "2083-07-04 BS (2026-10-21 AD)",
            DateLabel = "Date",
            IssueDateText = null,
            Reference = "Cash at counter",
            PartyHeading = "Received From",
            HeaderFields = [new PrintableFieldDto("Payment Mode", "Cash")],
            Sections =
            [
                new PrintableSectionDto(
                    "Payment Details",
                    [new PrintableColumnDto("Account", 4), new PrintableColumnDto("Amount", 1, AlignRight: true)],
                    [new PrintableRowDto(["1001 — Cash In Hand", "5,000.00"])],
                    new PrintableRowDto(["Net Debit", "5,000.00"])),
                new PrintableSectionDto(
                    "Payment For",
                    [
                        new PrintableColumnDto("Document", 3),
                        new PrintableColumnDto("Date", 1, AlignRight: true),
                        new PrintableColumnDto("Amount", 1, AlignRight: true),
                    ],
                    [
                        new PrintableRowDto(["Invoice INV0003/1002", "2083-06-24 BS (2026-10-10 AD)", "2,534.00"]),
                        new PrintableRowDto(["Invoice INV0004/1002", "2083-06-30 BS (2026-10-16 AD)", "1,466.00"]),
                    ],
                    new PrintableRowDto(["Allocated", "", "4,000.00"])),
            ],
            Summary =
            [
                new PrintableFieldDto("Amount", "5,000.00"),
                new PrintableFieldDto("Allocated", "4,000.00"),
                new PrintableFieldDto("Unallocated", "1,000.00", Emphasise: true),
            ],
            Terms = null,
            CustomFields = [],
            AmountInWords = "Rupees Five Thousand Only",
            // A money receipt is not a counted print (phase 67 counts invoices and credit notes), so no "Printed by".
            PrintedCopy = null,
        };

        var bytes = DocumentPdfRenderer.Render(receipt);
        var text = PdfText.Extract(bytes);

        foreach (var expected in new[]
                 {
                     "Moonbeam Trading", "PAN: 031564579", "CUSTOMER RECEIPT", "RCPT0007", "2083-07-04 BS (2026-10-21 AD)",
                     "Payment Mode", "Cash", "Received From", "C0001 — Adhi Kishan", "1001 — Cash In Hand", "Net Debit",
                     "Invoice INV0003/1002", "Invoice INV0004/1002", "Allocated", "4,000.00", "Unallocated", "1,000.00",
                     "Rupees Five Thousand Only",
                 })
        {
            Assert.Contains(expected, text, StringComparison.Ordinal);
        }

        var widths = Regex.Matches(System.Text.Encoding.Latin1.GetString(bytes), @"/MediaBox\s*\[\s*0\s+0\s+([0-9.]+)\s+([0-9.]+)\s*\]")
            .Select(m => float.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture))
            .ToList();
        Assert.Single(widths);
        Assert.InRange(widths[0], 226f, 227.5f);
    }

    [Fact]
    public void The_roll_draws_no_logo_and_no_signature_line()
    {
        var dto = Invoice(PrintLayout.Retail) with { OrganizationLogo = Png, ShowSignatureLine = true };
        var bytes = DocumentPdfRenderer.Render(dto);

        Assert.DoesNotContain("/Subtype /Image", System.Text.Encoding.Latin1.GetString(bytes), StringComparison.Ordinal);
        Assert.DoesNotContain("Prepared By", PdfText.Extract(bytes), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(PrintLayout.Modern)]
    [InlineData(PrintLayout.Classic)]
    public void The_a4_layouts_draw_the_logo_and_the_signature_line(PrintLayout layout)
    {
        var bytes = DocumentPdfRenderer.Render(Invoice(layout) with { OrganizationLogo = Png });

        Assert.Contains("/Subtype /Image", System.Text.Encoding.Latin1.GetString(bytes), StringComparison.Ordinal);
        Assert.Contains("Prepared By", PdfText.Extract(bytes), StringComparison.Ordinal);
        Assert.Contains("Approved By", PdfText.Extract(bytes), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(PrintLayout.Modern)]
    [InlineData(PrintLayout.Classic)]
    [InlineData(PrintLayout.Retail)]
    public void A_document_with_no_party_summary_or_words_still_renders(PrintLayout layout)
    {
        // A journal voucher's shape: no party, no summary, no words, one section with a single column.
        var dto = Invoice(layout) with
        {
            DocumentType = DocumentType.JournalVoucher,
            PartyHeading = null,
            PartyLabel = null,
            PartyAddress = null,
            PartyPan = null,
            Summary = [],
            AmountInWords = null,
            Terms = null,
            PrintedCopy = null,
            Sections = [new PrintableSectionDto("Empty", [new PrintableColumnDto("Account", 4)], [])],
        };

        Assert.Contains("INV0003/1002", Text(dto), StringComparison.Ordinal);
    }
}
