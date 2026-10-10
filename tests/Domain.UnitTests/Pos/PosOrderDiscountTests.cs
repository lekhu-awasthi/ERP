using ErpApp.Domain.Catalog;
using ErpApp.Domain.Pos;
using ErpApp.Domain.Sales;
using ErpApp.Domain.Tenancy;

namespace ErpApp.Domain.UnitTests.Pos;

/// <summary>
/// Phase 72 -- a discount and a waived service charge on a restaurant order: terms of the order, priced by its one
/// arithmetic (<see cref="PosOrder.FiguresOf"/>) and fixed from the first bill, so the bills still add up to the
/// order. Phase 59's table is the running example: 2 Chicken Momo (200, 10% service charge) and 2 Coke 250ml (60,
/// none), 13% VAT, 632.80 undiscounted. With 10% off the momo and 5% off the order it is 553.93, billed as 554.
/// </summary>
public class PosOrderDiscountTests
{
    private static readonly Guid User = Guid.NewGuid();
    private static readonly Guid Organization = Guid.NewGuid();
    private static readonly Guid Location = Guid.NewGuid();
    private static readonly IReadOnlyDictionary<Guid, decimal> NothingBilled = new Dictionary<Guid, decimal>();

    private static PosOrderLineQuantity Q(PosOrderLine line, decimal quantity) => new(line.Id, quantity);

    private static (PosOrder Order, PosOrderLine Momo, PosOrderLine Coke) Table(string code = "ORD0001")
    {
        var order = PosOrder.Open(
            Organization, Location, code, PosTab.DineIn, Guid.NewGuid(), 2, null, User, DateTimeOffset.UtcNow);
        order.Send(
            [
                new PosOrderNewLine(Guid.NewGuid(), null, 1m, 2m, 200m, VatRate.ThirteenPercentVat, 10m, PackagingFee.None, null, null),
                new PosOrderNewLine(Guid.NewGuid(), null, 1m, 2m, 60m, VatRate.ThirteenPercentVat, 0m, PackagingFee.None, null, null),
            ],
            [], User);
        return (order, order.Lines[0], order.Lines[1]);
    }

    /// <summary>What a test has billed: the planner's two inputs, kept as the invoices would keep them.</summary>
    private sealed class Billing
    {
        public Dictionary<Guid, PosOrderLineBilled> Lines { get; } = [];
        public PosOrderBilledTotals Totals { get; private set; } = PosOrderBilledTotals.None;

        public IReadOnlyDictionary<Guid, decimal> Invoiced => Lines.ToDictionary(x => x.Key, x => x.Value.Quantity);

        public PosOrderBillPlan Pay(PosOrder order, PosOrderSplit split, params PosOrderLineQuantity[] items)
        {
            var plan = PosOrderBill.Plan(order, Lines, Totals, split, items, 0, roundOff: true);

            foreach (var line in plan.Lines)
            {
                var before = Lines.GetValueOrDefault(line.Line.Id);
                Lines[line.Line.Id] = new PosOrderLineBilled(
                    (before?.Quantity ?? 0m) + line.Quantity, (before?.Figures ?? PosLineArithmetic.Figures.Zero) + line.Figures);
            }

            Totals = new PosOrderBilledTotals(Totals.Unrounded + plan.Unrounded, Totals.GrandTotal + plan.Total);
            order.SettleIfFullyBilled(Invoiced, DateTimeOffset.UtcNow);
            return plan;
        }
    }

    [Fact]
    public void A_line_discount_and_the_orders_price_every_figure_the_till_shows()
    {
        var (order, momo, coke) = Table();

        order.SetLineDiscount(momo.Id, 10m, NothingBilled);
        order.SetDiscount(5m, NothingBilled);

        // The momo: 400 less 10% is 360, less 5% is 342.00; service charge on what is left, 34.20; VAT on both.
        var momoFigures = order.FiguresOf(momo, 2m);
        Assert.Equal((342.00m, 34.20m, 48.91m), (momoFigures.Amount, momoFigures.ServiceChargeAmount, momoFigures.VatAmount));

        // The Coke has no discount of its own, so only the order's: 120 less 5%.
        var cokeFigures = order.FiguresOf(coke, 2m);
        Assert.Equal((114.00m, 0m, 14.82m), (cokeFigures.Amount, cokeFigures.ServiceChargeAmount, cokeFigures.VatAmount));

        Assert.Equal(553.93m, order.Estimate().Total);
        Assert.Equal(10m, momo.DiscountPct);
        Assert.Equal(0m, coke.DiscountPct);
        Assert.Equal(5m, order.DiscountPct);
    }

    /// <summary>Phase 65's rule survives a discount: each part priced at the same terms, the last of a line taking
    /// exactly what is left, rounding on the running total. 553.93 in halves is 277 and 277, and the momo's VAT
    /// (48.91) splits 24.45 / 24.46.</summary>
    [Fact]
    public void A_discounted_order_split_by_item_still_adds_up_to_the_order()
    {
        var (order, momo, coke) = Table();
        order.SetLineDiscount(momo.Id, 10m, NothingBilled);
        order.SetDiscount(5m, NothingBilled);
        var billing = new Billing();

        var first = billing.Pay(order, PosOrderSplit.Items, Q(momo, 1m), Q(coke, 1m));

        Assert.Equal(276.96m, first.Unrounded);
        Assert.Equal(277m, first.Total);
        Assert.Equal(554m, first.OrderTotal);
        Assert.Equal(24.45m, first.Lines.Single(x => x.Line.Id == momo.Id).Figures.VatAmount);

        var rest = billing.Pay(order, PosOrderSplit.Whole);

        Assert.Equal(276.97m, rest.Unrounded);
        Assert.Equal(277m, rest.Total);
        Assert.Equal(0.03m, rest.RoundOff);
        Assert.Equal(24.46m, rest.Lines.Single(x => x.Line.Id == momo.Id).Figures.VatAmount);
        Assert.Equal(34.20m, first.ServiceCharge + rest.ServiceCharge);
        Assert.Equal(554m, first.Total + rest.Total);
        Assert.Equal(PosOrderStatus.Settled, order.Status);
    }

    [Fact]
    public void The_orders_discount_and_its_waiver_are_fixed_once_any_of_it_is_billed_and_a_lines_once_that_line_is()
    {
        var (order, momo, coke) = Table();
        order.SetDiscount(5m, NothingBilled);
        var billing = new Billing();

        billing.Pay(order, PosOrderSplit.Items, Q(momo, 1m));

        var discount = Assert.Throws<InvalidOperationException>(() => order.SetDiscount(10m, billing.Invoiced));
        Assert.Contains("already billed, so its discount is fixed", discount.Message);
        Assert.Throws<InvalidOperationException>(() => order.WaiveServiceCharge(true, billing.Invoiced));
        Assert.Throws<InvalidOperationException>(() => order.SetLineDiscount(momo.Id, 10m, billing.Invoiced));

        // Setting what is already there changes nothing, so a repeated request is not a refusal.
        order.SetDiscount(5m, billing.Invoiced);
        order.WaiveServiceCharge(false, billing.Invoiced);

        // The Coke has nothing billed, so its own discount is still free to change.
        order.SetLineDiscount(coke.Id, 50m, billing.Invoiced);
        Assert.Equal(50m, coke.DiscountPct);
        Assert.Equal(5m, order.DiscountPct);
    }

    [Fact]
    public void A_waived_service_charge_bills_none_while_each_line_keeps_its_own_rate()
    {
        var (order, momo, _) = Table();

        order.WaiveServiceCharge(true, NothingBilled);

        Assert.True(order.ServiceChargeWaived);
        Assert.Equal(10m, momo.ServiceChargeRate);
        Assert.Equal(0m, order.ServiceChargeRateOf(momo));
        Assert.Equal(0m, order.FiguresOf(momo, 2m).ServiceChargeAmount);

        var plan = new Billing().Pay(order, PosOrderSplit.Whole);

        Assert.Equal(0m, plan.ServiceCharge);
        Assert.Equal(587.60m, plan.Unrounded);
        Assert.Equal(588m, plan.Total);

        // Changing their mind before any bill restores it.
        var (again, momoAgain, _) = Table("ORD0002");
        again.WaiveServiceCharge(true, NothingBilled);
        again.WaiveServiceCharge(false, NothingBilled);
        Assert.Equal(40m, again.FiguresOf(momoAgain, 2m).ServiceChargeAmount);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(100.01)]
    [InlineData(12.345)]
    public void A_discount_is_a_percentage_from_0_to_100_with_two_decimal_places(decimal discountPct)
    {
        var (order, momo, _) = Table();

        Assert.Throws<InvalidOperationException>(() => order.SetDiscount(discountPct, NothingBilled));
        Assert.Throws<InvalidOperationException>(() => order.SetLineDiscount(momo.Id, discountPct, NothingBilled));
    }

    [Fact]
    public void A_dish_on_the_house_is_a_100_percent_line_discount()
    {
        var (order, momo, coke) = Table();

        order.SetLineDiscount(coke.Id, 100m, NothingBilled);
        order.SetLineDiscount(momo.Id, 12.5m, NothingBilled);

        Assert.Equal(PosLineArithmetic.Figures.Zero, order.FiguresOf(coke, 2m));
        Assert.Equal(350.00m, order.FiguresOf(momo, 2m).Amount);
    }

    [Fact]
    public void A_parcel_keeps_its_lines_discount_and_one_changed_since_is_not_joined()
    {
        var (order, momo, _) = Table();
        order.SetLineDiscount(momo.Id, 10m, NothingBilled);

        var parcel = Assert.Single(order.MarkTakeAway(momo.Id, 1m, true, PackagingFee.None, User, PosBills.None).NewLines);
        Assert.Equal(10m, parcel.DiscountPct);

        order.SetLineDiscount(parcel.Id, 20m, NothingBilled);
        var second = Assert.Single(order.MarkTakeAway(momo.Id, 1m, true, PackagingFee.None, User, PosBills.None).NewLines);

        Assert.Equal(10m, second.DiscountPct);
        Assert.Equal(1m, order.QuantitiesOf(parcel).Net);
        Assert.Equal(1m, order.QuantitiesOf(second).Net);
    }

    [Fact]
    public void A_transfer_to_a_new_tab_carries_the_lines_discount_and_the_tabs_terms()
    {
        var (source, momo, _) = Table();
        source.SetLineDiscount(momo.Id, 10m, NothingBilled);
        source.SetDiscount(5m, NothingBilled);
        source.WaiveServiceCharge(true, NothingBilled);
        var target = PosOrder.Open(
            Organization, Location, "ORD0002", PosTab.DineIn, Guid.NewGuid(), 1, null, User, DateTimeOffset.UtcNow);

        var result = PosOrder.Transfer(source, target, [Q(momo, 1m)], User, PosBills.None, DateTimeOffset.UtcNow);

        var moved = Assert.Single(result.Target.NewLines);
        Assert.Equal(10m, moved.DiscountPct);
        Assert.Equal(5m, target.DiscountPct);
        Assert.True(target.ServiceChargeWaived);

        // The dish costs the same on either tab.
        Assert.Equal(source.FiguresOf(momo, 1m), target.FiguresOf(moved, 1m));
    }

    [Fact]
    public void A_transfer_between_tabs_on_different_terms_is_refused_and_one_on_the_same_terms_is_not()
    {
        var (source, momo, _) = Table();
        source.SetDiscount(5m, NothingBilled);
        var (target, _, _) = Table("ORD0002");

        var refused = Assert.Throws<InvalidOperationException>(() =>
            PosOrder.Transfer(source, target, [Q(momo, 1m)], User, PosBills.None, DateTimeOffset.UtcNow));
        Assert.Contains("ORD0002 is priced with no discount and service charge", refused.Message);
        Assert.Contains("ORD0001 with a 5% discount and service charge", refused.Message);
        Assert.Equal(0m, target.DiscountPct);

        target.SetDiscount(5m, NothingBilled);
        PosOrder.Transfer(source, target, [Q(momo, 1m)], User, PosBills.None, DateTimeOffset.UtcNow);

        Assert.Equal(3m, target.Lines.Where(x => x.Rate == 200m).Sum(x => target.QuantitiesOf(x).Net));
    }

    [Fact]
    public void A_bill_carries_the_orders_discount_on_its_header_and_the_lines_own_on_the_line()
    {
        var invoice = Invoice.CreatePosSale(
            Organization, Guid.NewGuid(), Guid.NewGuid(), new DateOnly(2026, 10, 9), Location, Guid.NewGuid(),
            PosTab.DineIn, discountPct: 5m);
        invoice.BillPosOrder(Guid.NewGuid());
        var figures = new PosLineArithmetic.Figures(171.00m, 17.10m, 0m, 24.45m);

        Assert.Throws<InvalidOperationException>(() => invoice.AddPosOrderLine(
            Guid.NewGuid(), Guid.NewGuid(), 1m, 200m, VatRate.ThirteenPercentVat, 0m, 100.5m, 0m, null, 1m, 10m, PackagingFee.None, figures));

        invoice.AddPosOrderLine(
            Guid.NewGuid(), Guid.NewGuid(), 1m, 200m, VatRate.ThirteenPercentVat, 0m, 10m, 0m, null, 1m, 10m, PackagingFee.None, figures);

        Assert.Equal(5m, invoice.DiscountPct);
        var line = Assert.Single(invoice.Lines);
        Assert.Equal(10m, line.DiscountPct);
        Assert.Equal(212.55m, invoice.GrandTotal);
    }
}
