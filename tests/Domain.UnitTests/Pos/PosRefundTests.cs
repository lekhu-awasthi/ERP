using ErpApp.Domain.Catalog;
using ErpApp.Domain.Common;
using ErpApp.Domain.Configuration;
using ErpApp.Domain.Sales;

namespace ErpApp.Domain.UnitTests.Pos;

/// <summary>
/// Phase 63 -- a refund at the till on the aggregate: its lines price exactly as the sale's did, its
/// payout is exactly what the caller says is owed back, and its print log counts like the sale's.
/// </summary>
public class PosRefundTests
{
    private static readonly Guid Momo = Guid.NewGuid();
    private static readonly Guid CashMode = Guid.NewGuid();
    private static readonly Guid Drawer = Guid.NewGuid();

    private static CreditNote Refund(string reason = "Wrong order") => CreditNote.CreatePosRefund(
        Guid.NewGuid(), Guid.NewGuid(), new DateOnly(2026, 10, 1), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
        "0001", 0m, reason);

    private static Invoice.TenderInput Cash(decimal amount) => new(CashMode, PaymentModeKind.Cash, Drawer, amount);

    [Theory]
    [InlineData(1, 0, 0)]
    [InlineData(3, 7.5, 0)]
    [InlineData(2, 0, 12.5)]
    [InlineData(7, 3.33, 5)]
    public void A_refund_line_of_the_whole_quantity_gives_back_exactly_what_the_sale_line_charged(
        decimal quantity, decimal lineDiscount, decimal billDiscount)
    {
        var sale = Invoice.CreatePosSale(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), new DateOnly(2026, 10, 1), Guid.NewGuid(), Guid.NewGuid(),
            Domain.Pos.PosTab.Retail, billDiscount);
        sale.AddPosLine(Momo, quantity, 199.99m, VatRate.ThirteenPercentVat, lineDiscount, null, 1, null, 10m);
        var sold = sale.Lines[0];

        var refund = CreditNote.CreatePosRefund(
            Guid.NewGuid(), Guid.NewGuid(), new DateOnly(2026, 10, 1), Guid.NewGuid(), Guid.NewGuid(), sale.Id,
            "0001", billDiscount, "Returned");
        refund.AddPosLine(Momo, quantity, 199.99m, VatRate.ThirteenPercentVat, lineDiscount, null, 1, null, 10m);
        var returned = refund.Lines[0];

        Assert.Equal(sold.Amount, returned.Amount);
        Assert.Equal(sold.ServiceChargeAmount, returned.ServiceChargeAmount);
        Assert.Equal(sold.VatAmount, returned.VatAmount);
        Assert.Equal(sold.LineTotal, returned.LineTotal);
    }

    [Fact]
    public void A_refund_carries_its_sale_as_reference_and_its_channel()
    {
        var refund = Refund();

        Assert.Equal(SalesChannel.Pos, refund.Channel);
        Assert.Equal(DocumentType.Invoice, refund.ReferrerType);
        Assert.Equal("0001", refund.Reference);
        Assert.Equal("Wrong order", refund.Reason);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void A_refund_says_why(string reason)
    {
        Assert.Throws<InvalidOperationException>(() => Refund(reason));
    }

    [Fact]
    public void The_payouts_must_come_to_exactly_what_is_owed_back()
    {
        var refund = Refund();
        refund.AddPosLine(Momo, 1, 200m, VatRate.ThirteenPercentVat, 0, null, 1, null, 10m);
        refund.SetRoundOff(0.40m);
        Assert.Equal(249m, refund.GrandTotal);

        Assert.Throws<InvalidOperationException>(() => refund.PayOut([Cash(200m)], 249m));
        Assert.Throws<InvalidOperationException>(() => refund.PayOut([Cash(249m)], 300m));

        refund.PayOut([Cash(100m)], 100m);
        Assert.Equal(100m, refund.PaidOutAmount);
        Assert.Equal(149m, refund.ToAccountAmount);

        // Paid once.
        Assert.Throws<InvalidOperationException>(() => refund.PayOut([Cash(100m)], 100m));
        Assert.Throws<InvalidOperationException>(() => refund.SetRoundOff(0m));
    }

    [Fact]
    public void A_refund_of_nothing_paid_records_no_payout()
    {
        var refund = Refund();
        refund.AddPosLine(Momo, 1, 200m, VatRate.ThirteenPercentVat, 0, null, 1, null, 10m);

        refund.PayOut([], 0m);

        Assert.Empty(refund.Payouts);
        Assert.Equal(refund.GrandTotal, refund.ToAccountAmount);
    }

    [Fact]
    public void An_erp_credit_note_takes_no_till_line_and_a_refund_takes_no_erp_line()
    {
        var erp = CreditNote.Create(Guid.NewGuid(), Guid.NewGuid(), new DateOnly(2026, 10, 1), null, null, null);
        Assert.Throws<InvalidOperationException>(() =>
            erp.AddPosLine(Momo, 1, 200m, VatRate.ThirteenPercentVat, 0, null, 1, null, 10m));
        Assert.Throws<InvalidOperationException>(() => erp.SetRoundOff(0.40m));

        var refund = Refund();
        Assert.Throws<InvalidOperationException>(() =>
            refund.AddLine(Momo, 1, 200m, VatRate.ThirteenPercentVat, 0, null, 1));
    }

    [Fact]
    public void A_round_off_is_whole_paisa_and_never_takes_a_refund_below_zero()
    {
        var refund = Refund();
        refund.AddPosLine(Momo, 1, 0.30m, VatRate.NoVat, 0, null, 1, null, 0m);

        Assert.Throws<InvalidOperationException>(() => refund.SetRoundOff(0.005m));
        Assert.Throws<InvalidOperationException>(() => refund.SetRoundOff(-0.31m));
        refund.SetRoundOff(-0.30m);
        Assert.Equal(0m, refund.GrandTotal);
    }

    [Fact]
    public void A_refund_print_is_counted_and_an_erp_or_voided_note_has_no_receipt()
    {
        var refund = Refund();
        refund.AddPosLine(Momo, 1, 200m, VatRate.ThirteenPercentVat, 0, null, 1, null, 10m);
        refund.PayOut([Cash(248.60m)], 248.60m);
        refund.Approve(Guid.NewGuid(), "CN0001");

        var first = CreditNotePrint.Record(refund, 0, Guid.NewGuid(), DateTimeOffset.UtcNow);
        var fifth = CreditNotePrint.Record(refund, 4, Guid.NewGuid(), DateTimeOffset.UtcNow);
        Assert.False(first.IsCopy);
        Assert.Equal(5, fifth.PrintNumber);
        Assert.True(fifth.IsCopy);

        var erp = CreditNote.Create(Guid.NewGuid(), Guid.NewGuid(), new DateOnly(2026, 10, 1), null, null, null);
        erp.AddLine(Momo, 1, 200m, VatRate.ThirteenPercentVat, 0, null, 1);
        erp.Approve(Guid.NewGuid(), "CN0002");
        Assert.Throws<InvalidOperationException>(() => CreditNotePrint.Record(erp, 0, Guid.NewGuid(), DateTimeOffset.UtcNow));

        refund.Void(Guid.NewGuid());
        Assert.Throws<InvalidOperationException>(() => CreditNotePrint.Record(refund, 2, Guid.NewGuid(), DateTimeOffset.UtcNow));
    }
}
