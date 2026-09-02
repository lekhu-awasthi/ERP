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

    // --- FR-5.8 export sales (Phase 23) --------------------------------------

    [Fact]
    public void AddLine_zero_rates_every_line_of_an_export_invoice()
    {
        var invoice = Invoice.Create(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Today(), null, null, null,
            discountPct: 0, isExport: true, exportCountry: "India",
            exportDeclarationNo: "EXP-1", exportDeclarationDate: Today());

        // The caller asks for 13% and for exempt; both are overridden. On the live reference product
        // the Tax selector is disabled outright once the export box is ticked, so neither choice is
        // even offered -- the aggregate is where that becomes an invariant rather than UI behaviour.
        invoice.AddLine(Guid.NewGuid(), 1m, 100m, VatRate.ThirteenPercentVat, discountPct: 0);
        invoice.AddLine(Guid.NewGuid(), 2m, 50m, VatRate.NoVat, discountPct: 0);

        Assert.All(invoice.Lines, line => Assert.Equal(VatRate.ZeroVat, line.VatRate));
        Assert.Equal(0m, invoice.Lines.Sum(l => l.VatAmount));
        Assert.Equal(200m, invoice.GrandTotal);
    }

    [Fact]
    public void SetExport_re_rates_lines_that_were_already_added()
    {
        var invoice = Invoice.Create(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Today(), null, null, null);
        invoice.AddLine(Guid.NewGuid(), 1m, 100m, VatRate.ThirteenPercentVat, discountPct: 0);
        Assert.Equal(13m, invoice.Lines[0].VatAmount);

        // The other ordering -- lines first, flag second. Both have to land in the same place, or a
        // user could bank 13% VAT on an export sale just by ticking the box last.
        invoice.SetExport(true, "India", "EXP-1", Today());

        Assert.All(invoice.Lines, line => Assert.Equal(VatRate.ZeroVat, line.VatRate));
        Assert.Equal(0m, invoice.Lines.Sum(l => l.VatAmount));
        Assert.Equal(100m, invoice.GrandTotal);
    }

    [Fact]
    public void SetExport_preserves_quantity_rate_and_line_discount_while_re_rating()
    {
        var invoice = Invoice.Create(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Today(), null, null, null, discountPct: 10);
        var productId = Guid.NewGuid();
        invoice.AddLine(productId, 3m, 200m, VatRate.ThirteenPercentVat, discountPct: 25);

        invoice.SetExport(true, "India", null, null);

        // Re-rating rebuilds the lines, so this pins that nothing else on them is lost in the rebuild.
        var line = Assert.Single(invoice.Lines);
        Assert.Equal(productId, line.ProductId);
        Assert.Equal(3m, line.Quantity);
        Assert.Equal(200m, line.Rate);
        Assert.Equal(25m, line.DiscountPct);
        Assert.Equal(405m, line.Amount); // 3 * 200, less 25% line discount, less 10% header discount
    }

    [Fact]
    public void Turning_the_export_flag_off_clears_its_detail_fields_but_leaves_line_rates_alone()
    {
        var invoice = Invoice.Create(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Today(), null, null, null,
            discountPct: 0, isExport: true, exportCountry: "India",
            exportDeclarationNo: "EXP-1", exportDeclarationDate: Today());
        invoice.AddLine(Guid.NewGuid(), 1m, 100m, VatRate.ThirteenPercentVat, discountPct: 0);

        invoice.SetExport(false, "India", "EXP-1", Today());

        Assert.False(invoice.IsExport);
        Assert.Null(invoice.ExportCountry);
        Assert.Null(invoice.ExportDeclarationNo);
        Assert.Null(invoice.ExportDeclarationDate);
        // The line stays zero-rated: clearing the flag does not guess a rate back, the user re-picks.
        Assert.Equal(VatRate.ZeroVat, invoice.Lines[0].VatRate);
    }

    [Fact]
    public void Create_ignores_export_details_when_the_flag_is_not_set()
    {
        var invoice = Invoice.Create(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Today(), null, null, null,
            discountPct: 0, isExport: false, exportCountry: "India",
            exportDeclarationNo: "EXP-1", exportDeclarationDate: Today());

        Assert.False(invoice.IsExport);
        Assert.Null(invoice.ExportCountry);
        Assert.Null(invoice.ExportDeclarationNo);
        Assert.Null(invoice.ExportDeclarationDate);
    }

    [Fact]
    public void An_export_invoice_can_leave_every_detail_field_empty()
    {
        // Live-confirmed: unlike PurchaseBill's import block, none of the three carries a required
        // marker on the reference product's form.
        var invoice = Invoice.Create(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Today(), null, null, null, discountPct: 0, isExport: true);

        Assert.True(invoice.IsExport);
        Assert.Null(invoice.ExportCountry);
        Assert.Null(invoice.ExportDeclarationNo);
        Assert.Null(invoice.ExportDeclarationDate);
    }

    private static DateOnly Today() => DateOnly.FromDateTime(DateTime.UtcNow);
}
