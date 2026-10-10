using ErpApp.Domain.Catalog;
using ErpApp.Domain.Pos;
using ErpApp.Domain.Sales;
using ErpApp.Domain.Tenancy;

namespace ErpApp.Domain.UnitTests.Pos;

/// <summary>
/// Phase 74 -- Amount discounts at both tills: a line's is rupees off each unit, a bill's or an order's is rupees off
/// the whole, spread over the lines by what each comes to (<see cref="PosLineArithmetic.Spread"/>). The Retail example
/// is the sale read live on the vendor on 2026-10-10: 3 momo at 333 with 10 off each and a tea at 37.50, 50 off the
/// bill, 13% VAT, which the vendor billed at 1,080.84 -- and so does this arithmetic.
/// </summary>
public class PosAmountDiscountTests
{
    private static readonly Guid User = Guid.NewGuid();
    private static readonly Guid Organization = Guid.NewGuid();
    private static readonly Guid Location = Guid.NewGuid();
    private static readonly IReadOnlyDictionary<Guid, decimal> NothingBilled = new Dictionary<Guid, decimal>();

    // ---- The arithmetic ------------------------------------------------------------------------------------

    [Fact]
    public void A_bill_amount_is_spread_by_what_each_line_comes_to_and_adds_up_exactly()
    {
        // The vendor's own spread of the same sale: 48.14 and 1.86.
        Assert.Equal([48.14m, 1.86m], PosLineArithmetic.Spread(50m, [969m, 37.50m]));

        // What rounding leaves over goes to the largest line, the first of equals.
        Assert.Equal([0.04m, 0.03m, 0.03m], PosLineArithmetic.Spread(0.10m, [1m, 1m, 1m]));
        Assert.Equal([0m, 0m], PosLineArithmetic.Spread(0m, [10m, 20m]));

        Assert.Throws<InvalidOperationException>(() => PosLineArithmetic.Spread(30.01m, [10m, 20m]));
        Assert.Throws<InvalidOperationException>(() => PosLineArithmetic.Spread(1.005m, [10m, 20m]));
    }

    [Fact]
    public void A_line_amount_is_off_each_unit_and_never_more_than_the_rate()
    {
        var figures = PosLineArithmetic.Compute(3m, 333m, 0.13m, 10m, 0m, 0m, 0m, 0m, PackagingFee.None);
        Assert.Equal((969m, 125.97m), (figures.Amount, figures.VatAmount));

        Assert.Throws<InvalidOperationException>(() =>
            PosLineArithmetic.Compute(1m, 333m, 0.13m, 333.01m, 0m, 0m, 0m, 0m, PackagingFee.None));
        Assert.Throws<InvalidOperationException>(() =>
            PosLineArithmetic.Compute(1m, 100m, 0.13m, 0m, 0m, 0m, 100.01m, 0m, PackagingFee.None));
    }

    // ---- A Retail sale ---------------------------------------------------------------------------------------

    private static Invoice Sale(decimal discountPct = 0m, decimal discountAmount = 0m) => Invoice.CreatePosSale(
        Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), new DateOnly(2026, 10, 10), Guid.NewGuid(), Guid.NewGuid(),
        PosTab.Retail, discountPct, discountAmount);

    private static Invoice.PosLineInput Line(decimal quantity, decimal rate, decimal perUnit = 0m, decimal pct = 0m) =>
        new(Guid.NewGuid(), quantity, rate, VatRate.ThirteenPercentVat, perUnit, pct, null, 1m, null, 0m, PackagingFee.None);

    [Fact]
    public void The_vendors_sale_comes_to_what_the_vendor_billed_with_each_line_carrying_its_share()
    {
        var sale = Sale(discountAmount: 50m);
        sale.AddPosLines([Line(3m, 333m, perUnit: 10m), Line(1m, 37.50m)]);

        var (momo, tea) = (sale.Lines[0], sale.Lines[1]);
        Assert.Equal((10m, 48.14m, 920.86m, 119.71m), (momo.DiscountPerUnit, momo.BillDiscountAmount, momo.Amount, momo.VatAmount));
        Assert.Equal((0m, 1.86m, 35.64m, 4.63m), (tea.DiscountPerUnit, tea.BillDiscountAmount, tea.Amount, tea.VatAmount));
        Assert.Equal(1080.84m, sale.GrandTotal);
        Assert.Equal(50m, sale.DiscountAmount);
    }

    [Fact]
    public void A_discount_is_a_percentage_or_an_amount_never_both()
    {
        Assert.Throws<InvalidOperationException>(() => Sale(discountPct: 5m, discountAmount: 5m));
        Assert.Throws<InvalidOperationException>(() => Sale().AddPosLines([Line(1m, 100m, perUnit: 5m, pct: 5m)]));

        // A bill with an amount spreads it over every line at once, so one line at a time is refused.
        var sale = Sale(discountAmount: 5m);
        Assert.Throws<InvalidOperationException>(() =>
            sale.AddPosLine(Guid.NewGuid(), 1m, 100m, VatRate.ThirteenPercentVat, 0m, null, 1m, null, 0m, PackagingFee.None));

        // And more than the bill comes to is not a discount.
        Assert.Throws<InvalidOperationException>(() => Sale(discountAmount: 100.01m).AddPosLines([Line(1m, 100m)]));
    }

    [Fact]
    public void A_refund_line_given_what_is_left_of_the_sale_line_returns_exactly_that()
    {
        var sale = Sale(discountAmount: 50m);
        sale.AddPosLines([Line(3m, 333m, perUnit: 10m), Line(1m, 37.50m)]);
        var momo = sale.Lines[0];

        var refund = CreditNote.CreatePosRefund(
            sale.OrganizationId, sale.ContactId, sale.Date, sale.LocationId!.Value, Guid.NewGuid(), sale.Id, "INV1", 0m,
            "Returned");
        var theRest = new PosLineArithmetic.Figures(613.91m, 0m, 0m, 79.81m);
        refund.AddPosLine(new CreditNote.PosLineInput(
            momo.ProductId, 2m, momo.Rate, momo.VatRate, momo.DiscountPerUnit, 0m, 32.09m, null, 1m, null, 0m,
            PackagingFee.None, theRest));

        var returned = Assert.Single(refund.Lines);
        Assert.Equal(theRest, returned.Figures);
        Assert.Equal((10m, 32.09m), (returned.DiscountPerUnit, returned.BillDiscountAmount));
        Assert.Equal(32.09m, refund.DiscountAmount);

        Assert.Throws<InvalidOperationException>(() => refund.AddPosLine(new CreditNote.PosLineInput(
            momo.ProductId, 1m, momo.Rate, momo.VatRate, momo.DiscountPerUnit, 0m, 0m, null, 1m, null, 0m,
            PackagingFee.None, new PosLineArithmetic.Figures(-0.01m, 0m, 0m, 0m))));
    }

    // ---- A restaurant order ---------------------------------------------------------------------------------

    /// <summary>Phase 59's table: 2 momo at 200 (10% service charge) and 2 Coke at 60, 520 before VAT.</summary>
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

    /// <summary>What a test has billed, as the invoices would keep it: quantities, figures and shares.</summary>
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
                    (before?.Quantity ?? 0m) + line.Quantity,
                    (before?.Figures ?? PosLineArithmetic.Figures.Zero) + line.Figures,
                    (before?.BillDiscountAmount ?? 0m) + line.BillDiscountShare);
            }

            // The bill command freezes the spread with the first bill.
            order.FixDiscountShares();
            Totals = new PosOrderBilledTotals(Totals.Unrounded + plan.Unrounded, Totals.GrandTotal + plan.Total);
            order.SettleIfFullyBilled(Invoiced, DateTimeOffset.UtcNow);
            return plan;
        }
    }

    [Fact]
    public void An_order_amount_is_spread_over_its_dishes_and_its_parts_take_their_share()
    {
        var (order, momo, coke) = Table();
        order.SetDiscount(PosDiscountKind.Amount, 52m, NothingBilled);

        // 52 over 400 and 120 is 40 and 12: the momo 360 + 36 + 51.48, the Coke 108 + 14.04.
        Assert.Equal((40m, 12m), (order.DiscountShareOf(momo), order.DiscountShareOf(coke)));
        Assert.Equal(569.52m, order.Estimate().Total);
        Assert.False(order.DiscountSharesFixed);

        var billing = new Billing();
        var first = billing.Pay(order, PosOrderSplit.Items, new PosOrderLineQuantity(momo.Id, 1m), new PosOrderLineQuantity(coke.Id, 1m));
        Assert.Equal([20m, 6m], first.Lines.Select(x => x.BillDiscountShare));
        Assert.True(order.DiscountSharesFixed);
        Assert.Equal((40m, 12m), (momo.OrderDiscountShare, coke.OrderDiscountShare));

        var rest = billing.Pay(order, PosOrderSplit.Whole);
        Assert.Equal([20m, 6m], rest.Lines.Select(x => x.BillDiscountShare));

        // The two bills add up to the order: 52 off it in all, and its rounded total.
        Assert.Equal(52m, first.Lines.Concat(rest.Lines).Sum(x => x.BillDiscountShare));
        Assert.Equal(570m, first.Total + rest.Total);
        Assert.Equal(PosOrderStatus.Settled, order.Status);
    }

    [Fact]
    public void Once_spread_a_dish_carrying_a_share_keeps_its_quantity_and_a_later_dish_carries_none()
    {
        var (order, momo, coke) = Table();
        order.SetDiscount(PosDiscountKind.Amount, 52m, NothingBilled);
        var billing = new Billing();
        billing.Pay(order, PosOrderSplit.Items, new PosOrderLineQuantity(momo.Id, 1m));

        var discard = Assert.Throws<InvalidOperationException>(() =>
            order.Discard([new PosOrderLineQuantity(coke.Id, 1m)], "Spilt", User, billing.Invoiced));
        Assert.Contains("12.00 of the order's discount", discard.Message);

        // The amount itself is a term fixed at the first bill, as a percentage is (phase 72).
        Assert.Throws<InvalidOperationException>(() => order.SetDiscount(PosDiscountKind.Amount, 60m, billing.Invoiced));

        // A dish sent after the first bill was not on the table the amount came off.
        order.Send([new PosOrderNewLine(Guid.NewGuid(), null, 1m, 1m, 100m, VatRate.ThirteenPercentVat, 0m, PackagingFee.None, null, null)], [], User);
        var later = order.Lines[2];
        Assert.Equal(0m, order.DiscountShareOf(later));
        Assert.Equal(100m, order.FiguresOf(later, 1m).Amount);
        order.Discard([new PosOrderLineQuantity(later.Id, 1m)], "Sent by mistake", User, billing.Invoiced);
    }

    [Fact]
    public void A_void_of_every_bill_lets_the_spread_follow_the_dishes_again()
    {
        var (order, momo, _) = Table();
        order.SetDiscount(PosDiscountKind.Amount, 52m, NothingBilled);
        order.FixDiscountShares();

        order.ReleaseDiscountShares(new Dictionary<Guid, decimal> { [momo.Id] = 1m });
        Assert.True(order.DiscountSharesFixed);

        order.ReleaseDiscountShares(NothingBilled);
        Assert.False(order.DiscountSharesFixed);
        Assert.Null(momo.OrderDiscountShare);
    }

    [Fact]
    public void An_order_amount_is_at_most_the_order_and_replaces_a_percentage()
    {
        var (order, momo, _) = Table();

        Assert.Throws<InvalidOperationException>(() => order.SetDiscount(PosDiscountKind.Amount, 520.01m, NothingBilled));

        order.SetDiscount(PosDiscountKind.Percent, 5m, NothingBilled);
        order.SetDiscount(PosDiscountKind.Amount, 10m, NothingBilled);
        Assert.Equal((0m, 10m), (order.DiscountPct, order.DiscountAmount));

        // A dish's amount is off each unit: 15 off each momo is 370, its service charge 37 and VAT on both.
        Assert.Throws<InvalidOperationException>(() =>
            order.SetLineDiscount(momo.Id, PosDiscountKind.Amount, 200.01m, NothingBilled));
        order.SetDiscount(PosDiscountKind.Amount, 0m, NothingBilled);
        order.SetLineDiscount(momo.Id, PosDiscountKind.Amount, 15m, NothingBilled);
        var figures = order.FiguresOf(momo, 2m);
        Assert.Equal((370m, 37m, 52.91m), (figures.Amount, figures.ServiceChargeAmount, figures.VatAmount));
        Assert.Equal((0m, 15m), (momo.DiscountPct, momo.DiscountPerUnit));
    }

    [Fact]
    public void Items_are_not_transferred_to_or_from_a_tab_with_an_amount_discount()
    {
        var (source, momo, _) = Table("ORD0001");
        var (target, _, _) = Table("ORD0002");
        source.SetDiscount(PosDiscountKind.Amount, 20m, NothingBilled);

        var refused = Assert.Throws<InvalidOperationException>(() =>
            PosOrder.Transfer(source, target, [new PosOrderLineQuantity(momo.Id, 1m)], User, NothingBilled, DateTimeOffset.UtcNow));
        Assert.Contains("ORD0001", refused.Message);

        Assert.Throws<InvalidOperationException>(() =>
            PosOrder.Transfer(target, source, [new PosOrderLineQuantity(target.Lines[0].Id, 1m)], User, NothingBilled, DateTimeOffset.UtcNow));
    }
}
