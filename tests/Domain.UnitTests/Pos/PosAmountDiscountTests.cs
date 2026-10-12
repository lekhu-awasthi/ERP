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
            "Returned", Guid.NewGuid());
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
    public void Once_spread_the_amount_is_fixed_and_a_later_dish_carries_none()
    {
        var (order, momo, _) = Table();
        order.SetDiscount(PosDiscountKind.Amount, 52m, NothingBilled);
        var billing = new Billing();
        billing.Pay(order, PosOrderSplit.Items, new PosOrderLineQuantity(momo.Id, 1m));

        // The amount itself is a term fixed at the first bill, as a percentage is (phase 72).
        Assert.Throws<InvalidOperationException>(() => order.SetDiscount(PosDiscountKind.Amount, 60m, billing.Invoiced));

        // A dish sent after the first bill was not on the table the amount came off, so discarding it moves nothing.
        order.Send([new PosOrderNewLine(Guid.NewGuid(), null, 1m, 1m, 100m, VatRate.ThirteenPercentVat, 0m, PackagingFee.None, null, null)], [], User);
        var later = order.Lines[2];
        Assert.Equal(0m, order.DiscountShareOf(later));
        Assert.Equal(100m, order.FiguresOf(later, 1m).Amount);
        order.Discard([new PosOrderLineQuantity(later.Id, 1m)], "Sent by mistake", User, billing.Lines);
        Assert.Equal(52m, order.DiscountAmount);
    }

    // ---- Phase 82: a discarded dish's share leaves with it ------------------------------------------------------

    [Fact]
    public void Before_the_first_bill_a_discard_leaves_the_amount_whole_and_spread_over_what_is_left()
    {
        var (order, momo, coke) = Table();
        order.SetDiscount(PosDiscountKind.Amount, 52m, NothingBilled);

        order.Discard([new PosOrderLineQuantity(coke.Id, 1m)], "Spilt", User, PosBills.None);

        // 52 over 400 and 60 is 45.22 and 6.78: the tab's term, not yet any dish's.
        Assert.Equal(52m, order.DiscountAmount);
        Assert.False(order.DiscountSharesFixed);
        Assert.Equal((45.22m, 6.78m), (order.DiscountShareOf(momo), order.DiscountShareOf(coke)));
    }

    [Fact]
    public void After_the_first_bill_a_discarded_dish_takes_its_quantitys_part_of_its_share_and_the_bills_add_up()
    {
        var (order, momo, coke) = Table();
        order.SetDiscount(PosDiscountKind.Amount, 52m, NothingBilled);
        var billing = new Billing();
        billing.Pay(order, PosOrderSplit.Items, new PosOrderLineQuantity(momo.Id, 1m));

        // One Coke of two carries 6 of the 12 spread over them; the momo is untouched.
        order.Discard([new PosOrderLineQuantity(coke.Id, 1m)], "Spilt", User, billing.Lines);
        Assert.Equal((40m, 6m, 46m), (momo.OrderDiscountShare, coke.OrderDiscountShare, order.DiscountAmount));

        // Every dish left costs what it did: the other momo and the other Coke bill their 20 and 6.
        var rest = billing.Pay(order, PosOrderSplit.Whole);
        Assert.Equal([20m, 6m], rest.Lines.Select(x => x.BillDiscountShare));
        Assert.Equal(46m, billing.Lines.Values.Sum(x => x.BillDiscountAmount));
        Assert.Equal(PosOrderStatus.Settled, order.Status);
    }

    [Fact]
    public void Discarding_all_a_dish_has_left_unbilled_takes_all_its_bills_left_of_its_share()
    {
        var (order, momo, coke) = Table();
        order.SetDiscount(PosDiscountKind.Amount, 52m, NothingBilled);
        var billing = new Billing();
        billing.Pay(order, PosOrderSplit.Items, new PosOrderLineQuantity(momo.Id, 1m), new PosOrderLineQuantity(coke.Id, 1m));

        // Both dishes in one discard: each takes what its bill left, 20 and 6, and the order keeps what was billed.
        order.Discard(
            [new PosOrderLineQuantity(momo.Id, 1m), new PosOrderLineQuantity(coke.Id, 1m)], "Guests left", User, billing.Lines);
        order.SettleIfFullyBilled(billing.Invoiced, DateTimeOffset.UtcNow);

        Assert.Equal((20m, 6m, 26m), (momo.OrderDiscountShare, coke.OrderDiscountShare, order.DiscountAmount));
        Assert.Equal(order.DiscountAmount, billing.Lines.Values.Sum(x => x.BillDiscountAmount));
        Assert.Equal(PosOrderStatus.Settled, order.Status);
    }

    [Fact]
    public void A_share_that_does_not_divide_leaves_the_last_unit_what_is_left()
    {
        // Three momo at 200 with 10 off the order: 10 over 600, frozen at the first bill.
        var order = Empty("ORD0001");
        order.Send([new PosOrderNewLine(Guid.NewGuid(), null, 1m, 3m, 200m, VatRate.ThirteenPercentVat, 0m, PackagingFee.None, null, null)], [], User);
        var momo = order.Lines[0];
        order.SetDiscount(PosDiscountKind.Amount, 10m, NothingBilled);
        var billing = new Billing();
        var first = billing.Pay(order, PosOrderSplit.Items, new PosOrderLineQuantity(momo.Id, 1m));
        Assert.Equal([3.33m], first.Lines.Select(x => x.BillDiscountShare));

        // 6.67 is left on two: the discarded one takes half (3.335, rounded away from zero), and the last bill the
        // 3.33 that remains of the dish's 10 -- not a second 3.34, or the order's parts would add up to 10.01.
        order.Discard([new PosOrderLineQuantity(momo.Id, 1m)], "Dropped", User, billing.Lines);
        Assert.Equal((6.66m, 6.66m), (momo.OrderDiscountShare, order.DiscountAmount));

        var rest = billing.Pay(order, PosOrderSplit.Whole);
        Assert.Equal([3.33m], rest.Lines.Select(x => x.BillDiscountShare));
        Assert.Equal(order.DiscountAmount, billing.Lines.Values.Sum(x => x.BillDiscountAmount));
    }

    [Fact]
    public void A_discard_leaves_the_rest_of_the_tab_billed_as_billing_the_dish_and_refunding_it_would()
    {
        // The way round a refused discard was to bill the Coke on its own and refund that bill in full. Its bill took
        // 6 of the Coke's 12, so what remained to bill is what the discard leaves.
        var (billedAndRefunded, bMomo, bCoke) = Table();
        billedAndRefunded.SetDiscount(PosDiscountKind.Amount, 52m, NothingBilled);
        var viaRefund = new Billing();
        viaRefund.Pay(billedAndRefunded, PosOrderSplit.Items, new PosOrderLineQuantity(bMomo.Id, 1m));
        var theCoke = viaRefund.Pay(billedAndRefunded, PosOrderSplit.Items, new PosOrderLineQuantity(bCoke.Id, 1m));
        var lastViaRefund = viaRefund.Pay(billedAndRefunded, PosOrderSplit.Whole);

        var (discarded, dMomo, dCoke) = Table("ORD0002");
        discarded.SetDiscount(PosDiscountKind.Amount, 52m, NothingBilled);
        var viaDiscard = new Billing();
        viaDiscard.Pay(discarded, PosOrderSplit.Items, new PosOrderLineQuantity(dMomo.Id, 1m));
        discarded.Discard([new PosOrderLineQuantity(dCoke.Id, 1m)], "Spilt", User, viaDiscard.Lines);
        var lastViaDiscard = viaDiscard.Pay(discarded, PosOrderSplit.Whole);

        Assert.Equal(
            lastViaRefund.Lines.Select(x => (x.Quantity, x.Figures, x.BillDiscountShare)),
            lastViaDiscard.Lines.Select(x => (x.Quantity, x.Figures, x.BillDiscountShare)));
        Assert.Equal(52m - theCoke.Lines.Single().BillDiscountShare, discarded.DiscountAmount);
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

    // ---- Phase 77: the share moves with the food ----------------------------------------------------------------

    private static PosOrder Empty(string code) => PosOrder.Open(
        Organization, Location, code, PosTab.DineIn, Guid.NewGuid(), 1, null, User, DateTimeOffset.UtcNow);

    [Fact]
    public void A_transfer_from_an_unbilled_tab_takes_the_dishes_share_and_the_two_discounts_add_up_to_the_first()
    {
        var (source, momo, _) = Table();
        source.SetDiscount(PosDiscountKind.Amount, 52m, NothingBilled);
        var before = source.FiguresOf(momo, 1m);
        var whole = source.Estimate().Total;
        var target = Empty("ORD0002");

        var result = PosOrder.Transfer(source, target, [new PosOrderLineQuantity(momo.Id, 1m)], User, PosBills.None, DateTimeOffset.UtcNow);

        // One momo of two carries 20 of its 40.
        var moved = Assert.Single(result.Target.NewLines);
        Assert.Equal((32m, 20m), (source.DiscountAmount, target.DiscountAmount));
        Assert.Equal(before, target.FiguresOf(moved, 1m));
        Assert.Equal(whole, source.Estimate().Total + target.Estimate().Total);
        Assert.False(target.DiscountSharesFixed);
    }

    [Fact]
    public void An_amount_joins_only_a_tab_taking_rupees_off_or_an_empty_one()
    {
        var (source, momo, _) = Table();
        source.SetDiscount(PosDiscountKind.Amount, 52m, NothingBilled);

        var (plain, _, _) = Table("ORD0002");
        var refused = Assert.Throws<InvalidOperationException>(() =>
            PosOrder.Transfer(source, plain, [new PosOrderLineQuantity(momo.Id, 1m)], User, PosBills.None, DateTimeOffset.UtcNow));
        Assert.Contains("ORD0001 with a discount of 52.00", refused.Message);

        var (percent, _, _) = Table("ORD0003");
        percent.SetDiscount(5m, NothingBilled);
        Assert.Throws<InvalidOperationException>(() =>
            PosOrder.Transfer(source, percent, [new PosOrderLineQuantity(momo.Id, 1m)], User, PosBills.None, DateTimeOffset.UtcNow));
        Assert.Throws<InvalidOperationException>(() =>
            PosOrder.Transfer(percent, source, [new PosOrderLineQuantity(percent.Lines[0].Id, 1m)], User, PosBills.None, DateTimeOffset.UtcNow));
        Assert.Equal(52m, source.DiscountAmount);

        // Another amount is the same kind of terms: it rises by what the dish brings.
        var (amount, _, _) = Table("ORD0004");
        amount.SetDiscount(PosDiscountKind.Amount, 10m, NothingBilled);
        PosOrder.Transfer(source, amount, [new PosOrderLineQuantity(momo.Id, 1m)], User, PosBills.None, DateTimeOffset.UtcNow);
        Assert.Equal((32m, 30m), (source.DiscountAmount, amount.DiscountAmount));
    }

    [Fact]
    public void A_transfer_from_a_billed_tab_takes_what_the_dishes_bills_left_and_every_bill_still_adds_up()
    {
        var (source, momo, coke) = Table();
        source.SetDiscount(PosDiscountKind.Amount, 52m, NothingBilled);
        var billing = new Billing();
        billing.Pay(source, PosOrderSplit.Items, new PosOrderLineQuantity(momo.Id, 1m), new PosOrderLineQuantity(coke.Id, 1m));
        Assert.True(source.DiscountSharesFixed);

        // The momo's bill took 20 of its 40; the momo still on the tab carries the other 20, and takes it along.
        var target = Empty("ORD0002");
        PosOrder.Transfer(source, target, [new PosOrderLineQuantity(momo.Id, 1m)], User, billing.Lines, DateTimeOffset.UtcNow);
        Assert.Equal((20m, 12m, 32m), (momo.OrderDiscountShare, coke.OrderDiscountShare, source.DiscountAmount));
        Assert.Equal(20m, target.DiscountAmount);

        // What is left of the source bills the coke's other 6; the target bills the momo's 20: 52 in all.
        var rest = billing.Pay(source, PosOrderSplit.Whole);
        Assert.Equal([6m], rest.Lines.Select(x => x.BillDiscountShare));
        Assert.Equal(PosOrderStatus.Settled, source.Status);

        var elsewhere = new Billing().Pay(target, PosOrderSplit.Whole);
        Assert.Equal([20m], elsewhere.Lines.Select(x => x.BillDiscountShare));
        Assert.Equal(52m, billing.Lines.Values.Sum(x => x.BillDiscountAmount) + elsewhere.Lines.Sum(x => x.BillDiscountShare));
    }

    [Fact]
    public void Part_of_a_billed_dish_takes_its_quantitys_part_and_a_billed_target_freezes_what_arrives()
    {
        var (source, momo, coke) = Table();
        source.SetDiscount(PosDiscountKind.Amount, 52m, NothingBilled);
        var billing = new Billing();
        billing.Pay(source, PosOrderSplit.Items, new PosOrderLineQuantity(coke.Id, 1m));

        var (target, _, targetCoke) = Table("ORD0002");
        target.SetDiscount(PosDiscountKind.Amount, 26m, NothingBilled);
        new Billing().Pay(target, PosOrderSplit.Items, new PosOrderLineQuantity(targetCoke.Id, 1m));

        var result = PosOrder.Transfer(source, target, [new PosOrderLineQuantity(momo.Id, 1m)], User, billing.Lines, DateTimeOffset.UtcNow);

        var arrived = Assert.Single(result.Target.NewLines);
        Assert.Equal((20m, 32m), (momo.OrderDiscountShare, source.DiscountAmount));
        Assert.Equal((20m, 46m), (arrived.OrderDiscountShare, target.DiscountAmount));
        Assert.Equal(180m, target.FiguresOf(arrived, 1m).Amount);
    }

    [Fact]
    public void A_parcel_from_a_billed_tab_takes_its_share_and_the_order_keeps_its_amount()
    {
        var (order, momo, coke) = Table();
        order.SetDiscount(PosDiscountKind.Amount, 52m, NothingBilled);
        var billing = new Billing();
        billing.Pay(order, PosOrderSplit.Items, new PosOrderLineQuantity(coke.Id, 1m));

        var parcel = Assert.Single(order.MarkTakeAway(momo.Id, 1m, true, PackagingFee.None, User, billing.Lines).NewLines);

        Assert.Equal((20m, 20m, 52m), (momo.OrderDiscountShare, parcel.OrderDiscountShare, order.DiscountAmount));

        var rest = billing.Pay(order, PosOrderSplit.Whole);
        Assert.Equal(52m, billing.Lines.Values.Sum(x => x.BillDiscountAmount));
        Assert.Contains(rest.Lines, x => x.Line.Id == parcel.Id && x.BillDiscountShare == 20m);
    }
}
