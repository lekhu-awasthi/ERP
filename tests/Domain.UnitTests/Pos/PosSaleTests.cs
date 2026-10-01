using ErpApp.Domain.Catalog;
using ErpApp.Domain.Configuration;
using ErpApp.Domain.Pos;
using ErpApp.Domain.Sales;

namespace ErpApp.Domain.UnitTests.Pos;

/// <summary>
/// Phase 61 -- the till sale's arithmetic and invariants on the aggregate, and the drawer's. The
/// numbers are phase 59's written service (erp-module-scan.md, "Numbers from the one service").
/// </summary>
public class PosSaleTests
{
    private static readonly Guid Momo = Guid.NewGuid();
    private static readonly Guid Coke = Guid.NewGuid();
    private static readonly Guid CashMode = Guid.NewGuid();
    private static readonly Guid CardMode = Guid.NewGuid();
    private static readonly Guid Drawer = Guid.NewGuid();
    private static readonly Guid Bank = Guid.NewGuid();

    private static Invoice Sale() => Invoice.CreatePosSale(
        Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), new DateOnly(2026, 10, 1), Guid.NewGuid(), Guid.NewGuid(),
        PosTab.DineIn);

    /// <summary>2 momo with a 10% service charge, 2 coke without -- the vendor's table.</summary>
    private static Invoice VendorTable()
    {
        var sale = Sale();
        sale.AddPosLine(Momo, 2, 200m, VatRate.ThirteenPercentVat, 0, null, 1, null, 10m);
        sale.AddPosLine(Coke, 2, 60m, VatRate.ThirteenPercentVat, 0, null, 1, null, 0m);
        return sale;
    }

    private static Invoice.TenderInput Cash(decimal amount) => new(CashMode, PaymentModeKind.Cash, Drawer, amount);

    private static Invoice.TenderInput Card(decimal amount) => new(CardMode, PaymentModeKind.Card, Bank, amount);

    [Fact]
    public void Service_charge_is_inside_the_VAT_base_line_by_line()
    {
        var sale = VendorTable();

        var momo = sale.Lines[0];
        Assert.Equal(400m, momo.Amount);
        Assert.Equal(40m, momo.ServiceChargeAmount);
        Assert.Equal(440m, momo.TaxableAmount);
        Assert.Equal(57.20m, momo.VatAmount);

        var coke = sale.Lines[1];
        Assert.Equal(0m, coke.ServiceChargeAmount);
        Assert.Equal(15.60m, coke.VatAmount);

        Assert.Equal(40m, sale.ServiceChargeTotal);
        Assert.Equal(632.80m, sale.GrandTotal);
    }

    [Theory]
    [InlineData(2, 2, 0.20, 633)] // 632.80 -> 633, the vendor's own bill
    [InlineData(1, 1, -0.40, 316)] // 316.40 -> 316; the vendor charged 317 (it rounds up)
    public void Round_off_brings_the_bill_to_the_nearest_rupee(int momo, int coke, decimal roundOff, decimal total)
    {
        var sale = Sale();
        sale.AddPosLine(Momo, momo, 200m, VatRate.ThirteenPercentVat, 0, null, 1, null, 10m);
        sale.AddPosLine(Coke, coke, 60m, VatRate.ThirteenPercentVat, 0, null, 1, null, 0m);

        sale.ApplyRoundOff();

        Assert.Equal(roundOff, sale.RoundOff);
        Assert.Equal(total, sale.GrandTotal);
    }

    [Fact]
    public void A_half_rupee_rounds_away_from_zero()
    {
        var sale = Sale();
        // 50 at 13% is 56.50.
        sale.AddPosLine(Coke, 1, 50m, VatRate.ThirteenPercentVat, 0, null, 1, null, 0m);

        sale.ApplyRoundOff();

        Assert.Equal(57m, sale.GrandTotal);
    }

    [Fact]
    public void A_line_added_after_rounding_clears_the_round_off()
    {
        var sale = VendorTable();
        sale.ApplyRoundOff();

        sale.AddPosLine(Coke, 1, 60m, VatRate.ThirteenPercentVat, 0, null, 1, null, 0m);

        Assert.Equal(0m, sale.RoundOff);
    }

    [Fact]
    public void Paying_in_full_with_change_leaves_nothing_on_credit()
    {
        var sale = VendorTable();
        sale.ApplyRoundOff();

        sale.Settle([Cash(500m), Card(500m)], 367m);

        Assert.Equal(1000m, sale.TenderedAmount);
        Assert.Equal(633m, sale.SettledAmount);
        Assert.Equal(0m, sale.CreditAmount);
    }

    [Fact]
    public void An_unsettled_remainder_is_the_credit()
    {
        var sale = VendorTable();
        sale.ApplyRoundOff();

        sale.Settle([Card(100m)], 0m);

        Assert.Equal(533m, sale.CreditAmount);
    }

    [Fact]
    public void Change_cannot_exceed_the_cash_handed_over()
    {
        var sale = VendorTable();
        sale.ApplyRoundOff();

        Assert.Throws<InvalidOperationException>(() => sale.Settle([Card(700m), Cash(10m)], 77m));
        Assert.Empty(sale.Tenders);
    }

    [Fact]
    public void Tenders_less_change_cannot_settle_more_than_the_bill()
    {
        var sale = VendorTable();
        sale.ApplyRoundOff();

        Assert.Throws<InvalidOperationException>(() => sale.Settle([Cash(1000m)], 300m));
        Assert.Empty(sale.Tenders);
        Assert.Equal(0m, sale.ChangeAmount);
    }

    [Fact]
    public void Change_is_only_given_on_a_bill_paid_in_full()
    {
        var sale = VendorTable();
        sale.ApplyRoundOff();

        Assert.Throws<InvalidOperationException>(() => sale.Settle([Cash(300m)], 10m));
    }

    [Fact]
    public void A_tender_is_whole_paisa()
    {
        var sale = VendorTable();

        Assert.Throws<InvalidOperationException>(() => sale.Settle([Cash(100.005m)], 0m));
    }

    [Fact]
    public void A_sale_is_paid_once()
    {
        var sale = VendorTable();
        sale.ApplyRoundOff();
        sale.Settle([Card(100m)], 0m);

        Assert.Throws<InvalidOperationException>(() => sale.Settle([Card(100m)], 0m));
        Assert.Throws<InvalidOperationException>(() =>
            sale.AddPosLine(Coke, 1, 60m, VatRate.ThirteenPercentVat, 0, null, 1, null, 0m));
    }

    [Fact]
    public void A_till_sale_takes_only_till_lines_and_is_never_an_export()
    {
        var sale = Sale();

        Assert.Throws<InvalidOperationException>(() =>
            sale.AddLine(Coke, 1, 60m, VatRate.ThirteenPercentVat, 0, null, 1));
        Assert.Throws<InvalidOperationException>(() => sale.SetExport(true, "IN", null, null));
    }

    [Fact]
    public void An_ERP_invoice_carries_no_till_figures_and_its_total_is_what_it_always_was()
    {
        var invoice = Invoice.Create(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), new DateOnly(2026, 10, 1), null, null, null);
        invoice.AddLine(Coke, 3, 33.3333m, VatRate.ThirteenPercentVat, 0, null, 1);

        Assert.Equal(SalesChannel.Erp, invoice.Channel);
        Assert.Equal(invoice.Lines.Sum(x => x.Amount + x.VatAmount), invoice.GrandTotal);
        Assert.Equal(invoice.GrandTotal, invoice.CreditAmount);
        Assert.Throws<InvalidOperationException>(() =>
            invoice.AddPosLine(Coke, 1, 60m, VatRate.ThirteenPercentVat, 0, null, 1, null, 0m));
        Assert.Throws<InvalidOperationException>(() => invoice.Settle([Cash(10m)], 0m));
    }

    [Fact]
    public void A_till_line_is_rounded_to_the_paisa_as_it_is_made()
    {
        var sale = Sale();
        sale.AddPosLine(Coke, 3, 33.3333m, VatRate.ThirteenPercentVat, 0, null, 1, null, 10m);

        var line = sale.Lines[0];
        Assert.Equal(100.00m, line.Amount);
        Assert.Equal(10.00m, line.ServiceChargeAmount);
        Assert.Equal(14.30m, line.VatAmount);
    }

    // ---- The drawer ----------------------------------------------------------------------

    private static PosSession Open(decimal openingFloat = 1000m, CashCount? count = null) =>
        PosSession.Open(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Drawer, "SES0001", openingFloat, count);

    [Fact]
    public void A_count_holds_only_the_notes_the_drawer_holds()
    {
        int[] drawer = [1000, 500, 100];

        Assert.Throws<InvalidOperationException>(() => CashCount.From([new DenominationCount(50, 1)], drawer));
        Assert.Throws<InvalidOperationException>(() =>
            CashCount.From([new DenominationCount(500, 1), new DenominationCount(500, 2)], drawer));
        Assert.Throws<InvalidOperationException>(() => CashCount.From([new DenominationCount(500, -1)], drawer));

        var count = CashCount.From(
            [new DenominationCount(100, 3), new DenominationCount(1000, 0), new DenominationCount(500, 2)], drawer);
        Assert.Equal(1300m, count.Total);
        Assert.Equal("500x2,100x3", count.Serialize());
        Assert.Equal(count.Serialize(), CashCount.Parse(count.Serialize()).Serialize());
    }

    [Fact]
    public void The_opening_float_is_what_the_counted_notes_add_up_to()
    {
        var count = CashCount.From([new DenominationCount(500, 2)], [500]);

        Assert.Throws<InvalidOperationException>(() => Open(900m, count));
        Assert.Equal(1000m, Open(1000m, count).OpeningFloat);
    }

    [Fact]
    public void A_cash_movement_names_somewhere_other_than_the_drawer()
    {
        var session = Open();

        Assert.Throws<InvalidOperationException>(() =>
            session.RecordCashMovement(PosCashMovementDirection.Out, 100m, Drawer, null, Guid.NewGuid()));
        Assert.Throws<InvalidOperationException>(() =>
            session.RecordCashMovement(PosCashMovementDirection.Out, 0m, Guid.NewGuid(), null, Guid.NewGuid()));

        var movement = session.RecordCashMovement(
            PosCashMovementDirection.Out, 100m, Guid.NewGuid(), "Vegetables", Guid.NewGuid());
        Assert.Equal(-100m, movement.SignedAmount);
        Assert.Single(session.CashMovements);
    }

    [Fact]
    public void A_difference_at_the_close_needs_a_note_and_the_close_freezes_both_figures()
    {
        var session = Open();

        var refused = Assert.Throws<InvalidOperationException>(() => session.Close(1249m, 1240m, null, null));
        Assert.Contains("short by 9.00", refused.Message, StringComparison.Ordinal);
        Assert.Equal(PosSessionStatus.Open, session.Status);

        session.Close(1249m, 1240m, null, "Coins short");

        Assert.Equal(PosSessionStatus.Closed, session.Status);
        Assert.Equal(1249m, session.ExpectedCash);
        Assert.Equal(-9m, session.CashDifference);
    }

    [Fact]
    public void A_closed_session_takes_nothing_more()
    {
        var session = Open();
        session.Close(1000m, 1000m, null, null);

        Assert.Throws<InvalidOperationException>(session.RecordActivity);
        Assert.Throws<InvalidOperationException>(() =>
            session.RecordCashMovement(PosCashMovementDirection.In, 10m, Guid.NewGuid(), null, Guid.NewGuid()));
        Assert.Throws<InvalidOperationException>(() => session.Close(1000m, 1000m, null, null));
    }
}
