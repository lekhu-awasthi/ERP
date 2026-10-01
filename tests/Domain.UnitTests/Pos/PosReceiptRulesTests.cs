using ErpApp.Domain.Catalog;
using ErpApp.Domain.Common;
using ErpApp.Domain.Configuration;
using ErpApp.Domain.Pos;
using ErpApp.Domain.Sales;

namespace ErpApp.Domain.UnitTests.Pos;

/// <summary>
/// Phase 62 -- what the aggregate itself decides about a receipt: an abbreviated tax invoice's limit
/// (VAT Rules Rule 18(6)), the print count that marks a copy (Procedure Related to Computerized
/// Invoicing 2072, §6), and the total in words.
/// </summary>
public class PosReceiptRulesTests
{
    private static readonly Guid Drawer = Guid.NewGuid();

    private static Invoice Sale(decimal rate, decimal quantity = 1)
    {
        var sale = Invoice.CreatePosSale(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), new DateOnly(2026, 10, 1), Guid.NewGuid(), Guid.NewGuid(),
            PosTab.Retail);
        sale.AddPosLine(Guid.NewGuid(), quantity, rate, VatRate.NoVat, 0, null, 1, null, 0m);
        return sale;
    }

    private static void PayInCash(Invoice sale) =>
        sale.Settle([new Invoice.TenderInput(Guid.NewGuid(), PaymentModeKind.Cash, Drawer, sale.GrandTotal)], 0m);

    // ---- Abbreviated tax invoice ----------------------------------------------------------------

    [Fact]
    public void A_bill_of_exactly_ten_thousand_may_be_abbreviated()
    {
        var sale = Sale(10_000m);
        PayInCash(sale);

        sale.IssueAsAbbreviatedTaxInvoice();

        Assert.True(sale.IsAbbreviatedTaxInvoice);
    }

    [Fact]
    public void A_bill_a_paisa_over_ten_thousand_may_not()
    {
        var sale = Sale(10_000.01m);
        PayInCash(sale);

        var refused = Assert.Throws<InvalidOperationException>(sale.IssueAsAbbreviatedTaxInvoice);
        Assert.Contains("Rule 18(6)", refused.Message, StringComparison.Ordinal);
        Assert.False(sale.IsAbbreviatedTaxInvoice);
    }

    [Fact]
    public void A_bill_is_abbreviated_only_once_it_is_paid_because_its_total_is_final_only_then()
    {
        var sale = Sale(100m);

        Assert.Throws<InvalidOperationException>(sale.IssueAsAbbreviatedTaxInvoice);
    }

    [Fact]
    public void An_erp_invoice_is_never_abbreviated()
    {
        var invoice = Invoice.Create(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), new DateOnly(2026, 10, 1), null, null, null,
            new DateOnly(2026, 10, 1));
        invoice.AddLine(Guid.NewGuid(), 1, 100m, VatRate.NoVat, 0, null, 1m);

        Assert.Throws<InvalidOperationException>(invoice.IssueAsAbbreviatedTaxInvoice);
        Assert.False(invoice.IsAbbreviatedTaxInvoice);
    }

    // ---- The print count ----------------------------------------------------------------------

    private static Invoice ApprovedSale()
    {
        var sale = Sale(100m);
        PayInCash(sale);
        sale.Approve(Guid.NewGuid(), "INV0001");
        return sale;
    }

    [Fact]
    public void The_first_print_is_number_one_and_not_a_copy()
    {
        var print = InvoicePrint.Record(ApprovedSale(), 0, Guid.NewGuid(), DateTimeOffset.UtcNow);

        Assert.Equal(1, print.PrintNumber);
        Assert.False(print.IsCopy);
    }

    [Fact]
    public void Four_copies_later_the_bill_says_it_has_been_printed_five_times()
    {
        // The procedure's own example: after four "copy of original", the last one indicates five prints.
        var print = InvoicePrint.Record(ApprovedSale(), 4, Guid.NewGuid(), DateTimeOffset.UtcNow);

        Assert.Equal(5, print.PrintNumber);
        Assert.True(print.IsCopy);
    }

    [Fact]
    public void A_voided_sale_or_an_erp_invoice_prints_no_receipt()
    {
        var voided = ApprovedSale();
        voided.Void(Guid.NewGuid());
        Assert.Throws<InvalidOperationException>(() => InvoicePrint.Record(voided, 0, Guid.NewGuid(), DateTimeOffset.UtcNow));

        var erp = Invoice.Create(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), new DateOnly(2026, 10, 1), null, null, null,
            new DateOnly(2026, 10, 1));
        erp.AddLine(Guid.NewGuid(), 1, 100m, VatRate.NoVat, 0, null, 1m);
        erp.Approve(Guid.NewGuid(), "INV0002");
        Assert.Throws<InvalidOperationException>(() => InvoicePrint.Record(erp, 0, Guid.NewGuid(), DateTimeOffset.UtcNow));
    }

    [Fact]
    public void An_unapproved_sale_prints_no_receipt()
    {
        var draft = Sale(100m);
        Assert.Throws<InvalidOperationException>(() => InvoicePrint.Record(draft, 0, Guid.NewGuid(), DateTimeOffset.UtcNow));
    }

    // ---- Amount in words ----------------------------------------------------------------------

    [Theory]
    [InlineData("0", "Rupees Zero Only")]
    [InlineData("633", "Rupees Six Hundred Thirty Three Only")]
    [InlineData("248.60", "Rupees Two Hundred Forty Eight and Sixty Paisa Only")]
    [InlineData("10000", "Rupees Ten Thousand Only")]
    [InlineData("100000", "Rupees One Lakh Only")]
    [InlineData("123456.70", "Rupees One Lakh Twenty Three Thousand Four Hundred Fifty Six and Seventy Paisa Only")]
    [InlineData("12500000", "Rupees One Crore Twenty Five Lakh Only")]
    [InlineData("1000000000", "Rupees One Arab Only")]
    [InlineData("0.05", "Rupees Zero and Five Paisa Only")]
    [InlineData("19.995", "Rupees Twenty Only")]
    public void Amounts_read_in_lakhs_and_crores(string amount, string expected)
    {
        Assert.Equal(expected, AmountInWords.Rupees(decimal.Parse(amount, System.Globalization.CultureInfo.InvariantCulture)));
    }
}
