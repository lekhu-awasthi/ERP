using ErpApp.Domain.Catalog;
using ErpApp.Domain.Sales;

namespace ErpApp.Domain.UnitTests.Sales;

/// <summary>Phase 16b: proves InvoiceLine.Amount/VatAmount fold in both the line's own DiscountPct
/// and the header Invoice.DiscountPct (line discount first, then header discount, VAT computed on
/// what's left) -- the exact formula confirmed live against the reference product's Totals panel
/// (Sub Total -> Discount% -> Taxable Total -> VAT). CreditNote/Quotation/SalesOrder/PurchaseOrder/
/// PurchaseBill/DebitNote all share this identical formula; Invoice is the representative case.</summary>
public class InvoiceTests
{
    [Fact]
    public void AddLine_with_no_discount_computes_plain_amount_and_vat()
    {
        var invoice = Invoice.Create(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Today(), null, null, null);

        invoice.AddLine(Guid.NewGuid(), 10m, 100m, VatRate.ThirteenPercentVat, discountPct: 0);

        var line = Assert.Single(invoice.Lines);
        Assert.Equal(1000m, line.Amount);
        Assert.Equal(130m, line.VatAmount);
    }

    [Fact]
    public void AddLine_applies_the_lines_own_discount_before_vat()
    {
        var invoice = Invoice.Create(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Today(), null, null, null);

        // Qty 10 * Rate 100 = 1000 gross, 10% line discount -> 900 net, VAT 13% of 900 = 117.
        invoice.AddLine(Guid.NewGuid(), 10m, 100m, VatRate.ThirteenPercentVat, discountPct: 10);

        var line = Assert.Single(invoice.Lines);
        Assert.Equal(900m, line.Amount);
        Assert.Equal(117m, line.VatAmount);
    }

    [Fact]
    public void AddLine_applies_the_header_discount_on_top_of_the_lines_own_discount()
    {
        // Matches the live-confirmed worked example: Qty 10 * Rate 1000 = 10,000 gross, 10% line
        // discount -> 9,000, 5% header discount -> 8,550 taxable, VAT 13% of 8,550 = 1,111.50.
        var invoice = Invoice.Create(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Today(), null, null, null, discountPct: 5);

        invoice.AddLine(Guid.NewGuid(), 10m, 1000m, VatRate.ThirteenPercentVat, discountPct: 10);

        var line = Assert.Single(invoice.Lines);
        Assert.Equal(8550m, line.Amount);
        Assert.Equal(1111.50m, line.VatAmount);
        Assert.Equal(9661.50m, invoice.GrandTotal);
    }

    [Fact]
    public void AddLine_with_only_a_header_discount_and_no_line_discount()
    {
        var invoice = Invoice.Create(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Today(), null, null, null, discountPct: 20);

        invoice.AddLine(Guid.NewGuid(), 1m, 500m, VatRate.NoVat, discountPct: 0);

        var line = Assert.Single(invoice.Lines);
        Assert.Equal(400m, line.Amount);
        Assert.Equal(0m, line.VatAmount);
    }

    [Fact]
    public void UpdateHeader_changing_discount_pct_affects_lines_added_afterward_not_existing_ones()
    {
        var invoice = Invoice.Create(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Today(), null, null, null);
        invoice.AddLine(Guid.NewGuid(), 1m, 100m, VatRate.NoVat, discountPct: 0);

        invoice.UpdateHeader(invoice.ContactId, invoice.WarehouseId, invoice.Date, invoice.Reference, discountPct: 50);
        invoice.AddLine(Guid.NewGuid(), 1m, 100m, VatRate.NoVat, discountPct: 0);

        Assert.Equal(100m, invoice.Lines[0].Amount);
        Assert.Equal(50m, invoice.Lines[1].Amount);
    }

    [Theory]
    [InlineData(-0.01)]
    [InlineData(100.01)]
    public void Create_rejects_a_header_discount_outside_0_to_100(decimal discountPct)
    {
        Assert.Throws<InvalidOperationException>(() =>
            Invoice.Create(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Today(), null, null, null, discountPct));
    }

    [Theory]
    [InlineData(-0.01)]
    [InlineData(100.01)]
    public void AddLine_rejects_a_line_discount_outside_0_to_100(decimal discountPct)
    {
        var invoice = Invoice.Create(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Today(), null, null, null);

        Assert.Throws<InvalidOperationException>(() =>
            invoice.AddLine(Guid.NewGuid(), 1m, 100m, VatRate.NoVat, discountPct));
    }

    private static DateOnly Today() => DateOnly.FromDateTime(DateTime.UtcNow);
}
