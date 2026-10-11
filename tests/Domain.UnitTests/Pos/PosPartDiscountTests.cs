using ErpApp.Domain.Catalog;
using ErpApp.Domain.Pos;
using ErpApp.Domain.Sales;
using ErpApp.Domain.Tenancy;

namespace ErpApp.Domain.UnitTests.Pos;

/// <summary>
/// Phase 83 -- a discount on one guest's part, once splitting has begun (phase 72 § 5). The part's own discount is
/// taken after the order's terms and spread over the part's lines; the order's terms stay fixed. The rule every test
/// here comes back to is phase 82's: what does the way round leave? Without this phase a guest's discount could only be
/// given by refunding part of their bill, which leaves every other guest's bill exactly as it was. So the next guest's
/// bill must be the same with an earlier guest's discount as without it, to the paisa -- the test is a twin tab.
/// </summary>
public class PosPartDiscountTests
{
    private static readonly Guid User = Guid.NewGuid();
    private static readonly Guid Organization = Guid.NewGuid();
    private static readonly Guid Location = Guid.NewGuid();
    private static readonly IReadOnlyDictionary<Guid, decimal> NothingBilled = new Dictionary<Guid, decimal>();

    /// <summary>Phase 59's table: 2 momo at 200 (10% service charge) and 2 Coke at 60, 13% VAT.</summary>
    private static (PosOrder Order, PosOrderLine Momo, PosOrderLine Coke) Table(string code = "ORD0001", decimal momo = 2m)
    {
        var order = PosOrder.Open(
            Organization, Location, code, PosTab.DineIn, Guid.NewGuid(), 2, null, User, DateTimeOffset.UtcNow);
        order.Send(
            [
                new PosOrderNewLine(Guid.NewGuid(), null, 1m, momo, 200m, VatRate.ThirteenPercentVat, 10m, PackagingFee.None, null, null),
                new PosOrderNewLine(Guid.NewGuid(), null, 1m, 2m, 60m, VatRate.ThirteenPercentVat, 0m, PackagingFee.None, null, null),
            ],
            [], User);
        return (order, order.Lines[0], order.Lines[1]);
    }

    /// <summary>What a test has billed, kept as the reader keeps it (<c>PosOrderBilling</c>): a line's Bill Discount
    /// is the order's share only, and its bills' own discounts are a second figure.</summary>
    private sealed class Billing
    {
        public Dictionary<Guid, PosOrderLineBilled> Lines { get; } = [];
        public PosOrderBilledTotals Totals { get; private set; } = PosOrderBilledTotals.None;
        private readonly List<PosOrderBillPlan> _bills = [];

        public IReadOnlyDictionary<Guid, decimal> Invoiced => Lines.ToDictionary(x => x.Key, x => x.Value.Quantity);

        public PosOrderBillPlan Pay(
            PosOrder order, PosOrderSplit split, PosPartDiscount? discount = null, int parts = 0,
            params PosOrderLineQuantity[] items)
        {
            var plan = PosOrderBill.Plan(order, Lines, Totals, split, items, parts, roundOff: true, discount);
            _bills.Add(plan);
            Rebuild();

            order.FixDiscountShares();
            order.SettleIfFullyBilled(Invoiced, DateTimeOffset.UtcNow);
            return plan;
        }

        /// <summary>A void: the bill counts nowhere any more, and nothing about it is written back anywhere.</summary>
        public void Void(PosOrderBillPlan bill)
        {
            _bills.Remove(bill);
            Rebuild();
        }

        private void Rebuild()
        {
            Lines.Clear();
            foreach (var line in _bills.SelectMany(x => x.Lines))
            {
                var before = Lines.GetValueOrDefault(line.Line.Id);
                Lines[line.Line.Id] = new PosOrderLineBilled(
                    (before?.Quantity ?? 0m) + line.Quantity,
                    (before?.Figures ?? PosLineArithmetic.Figures.Zero) + line.Figures,
                    (before?.BillDiscountAmount ?? 0m) + line.BillDiscountShare,
                    (before?.PartDiscountAmount ?? 0m) + line.PartDiscountShare);
            }

            Totals = new PosOrderBilledTotals(_bills.Sum(x => x.Unrounded), _bills.Sum(x => x.Total));
        }
    }

    private static PosOrderLineQuantity Of(PosOrderLine line, decimal quantity) => new(line.Id, quantity);

    private static PosPartDiscount Rupees(decimal value) => new(PosDiscountKind.Amount, value);

    private static PosPartDiscount Percent(decimal value) => new(PosDiscountKind.Percent, value);

    [Fact]
    public void A_guests_discount_is_spread_over_their_part_and_taken_after_the_orders_terms()
    {
        var (order, momo, coke) = Table();
        var billing = new Billing();

        // 26 off a momo and a Coke at 200 and 60 is 20 and 6: the momo 180 + 18 + 25.74, the Coke 54 + 7.02.
        var first = billing.Pay(order, PosOrderSplit.Items, Rupees(26m), items: [Of(momo, 1m), Of(coke, 1m)]);

        Assert.Equal([20m, 6m], first.Lines.Select(x => x.PartDiscountShare));
        Assert.Equal([0m, 0m], first.Lines.Select(x => x.BillDiscountShare));
        Assert.Equal(26m, first.PartDiscount);
        Assert.Equal(
            [new PosLineArithmetic.Figures(180m, 18m, 0m, 25.74m), new PosLineArithmetic.Figures(54m, 0m, 0m, 7.02m)],
            first.Lines.Select(x => x.Figures));
        Assert.Equal((284.76m, 285m), (first.Unrounded, first.Total));

        // And the order's own terms did not move: nothing is fixed by a guest's discount.
        Assert.Equal((0m, 0m), (order.DiscountPct, order.DiscountAmount));
        Assert.False(order.DiscountSharesFixed);
    }

    [Fact]
    public void The_next_guests_bill_is_what_it_would_have_been_without_the_first_guests_discount()
    {
        var (discounted, dMomo, dCoke) = Table();
        var withDiscount = new Billing();
        withDiscount.Pay(discounted, PosOrderSplit.Items, Rupees(26m), items: [Of(dMomo, 1m), Of(dCoke, 1m)]);
        var nextWith = withDiscount.Pay(discounted, PosOrderSplit.Whole);

        var (plain, pMomo, pCoke) = Table("ORD0002");
        var withoutDiscount = new Billing();
        var firstWithout = withoutDiscount.Pay(plain, PosOrderSplit.Items, items: [Of(pMomo, 1m), Of(pCoke, 1m)]);
        var nextWithout = withoutDiscount.Pay(plain, PosOrderSplit.Whole);

        // The last of each line takes what is left of it at the order's terms, and nothing of the first guest's 26.
        Assert.Equal(
            nextWithout.Lines.Select(x => (x.Quantity, x.Figures, x.BillDiscountShare, x.PartDiscountShare)),
            nextWith.Lines.Select(x => (x.Quantity, x.Figures, x.BillDiscountShare, x.PartDiscountShare)));
        Assert.Equal(316.40m, nextWith.Unrounded);
        Assert.Equal(PosOrderStatus.Settled, discounted.Status);

        // The bills add up to the order less what the guest was given: 26 of amount, and the service charge and VAT
        // that would have been charged on it.
        Assert.Equal(632.80m, firstWithout.Unrounded + nextWithout.Unrounded);
        Assert.Equal(601.16m, withDiscount.Totals.Unrounded);
        Assert.Equal(discounted.Estimate(withDiscount.Lines).Total, withDiscount.Totals.Unrounded);
        Assert.Equal((601m, 633m), (withDiscount.Totals.GrandTotal, withoutDiscount.Totals.GrandTotal));
    }

    [Fact]
    public void A_dish_split_in_parts_keeps_its_last_part_at_the_orders_terms()
    {
        // Three momo, one each: the first guest takes 15% off theirs, the second nothing, the third the last.
        var (discounted, dMomo, _) = Table(momo: 3m);
        var withDiscount = new Billing();
        var first = withDiscount.Pay(discounted, PosOrderSplit.Items, Percent(15m), items: [Of(dMomo, 1m)]);
        withDiscount.Pay(discounted, PosOrderSplit.Items, items: [Of(dMomo, 1m)]);
        var last = withDiscount.Pay(discounted, PosOrderSplit.Items, items: [Of(dMomo, 1m)]);

        var (plain, pMomo, _) = Table("ORD0002", momo: 3m);
        var withoutDiscount = new Billing();
        withoutDiscount.Pay(plain, PosOrderSplit.Items, items: [Of(pMomo, 1m)]);
        withoutDiscount.Pay(plain, PosOrderSplit.Items, items: [Of(pMomo, 1m)]);
        var lastWithout = withoutDiscount.Pay(plain, PosOrderSplit.Items, items: [Of(pMomo, 1m)]);

        // 15% of 200 is 30: 170 + 17 + 24.31.
        Assert.Equal(30m, first.PartDiscount);
        Assert.Equal(new PosLineArithmetic.Figures(170m, 17m, 0m, 24.31m), first.Lines.Single().Figures);
        Assert.Equal(lastWithout.Lines.Single().Figures, last.Lines.Single().Figures);

        // The momo line's three parts add up to the whole line priced less what its bills gave off it.
        Assert.Equal(
            discounted.FiguresOf(dMomo, 3m, 30m),
            withDiscount.Lines[dMomo.Id].Figures);
    }

    [Fact]
    public void A_discount_on_the_last_part_comes_off_what_is_left_of_the_line()
    {
        var (order, momo, _) = Table(momo: 3m);
        var billing = new Billing();
        billing.Pay(order, PosOrderSplit.Items, items: [Of(momo, 1m)]);

        // The last two momo with 50 off: what is left of the line (400 + 40 + 57.20) less 50 and its charges.
        var last = billing.Pay(order, PosOrderSplit.Items, Rupees(50m), items: [Of(momo, 2m)]);

        Assert.Equal(50m, last.PartDiscount);
        Assert.Equal(new PosLineArithmetic.Figures(350m, 35m, 0m, 50.05m), last.Lines.Single().Figures);
        Assert.Equal(order.FiguresOf(momo, 3m, 50m), billing.Lines[momo.Id].Figures);
    }

    [Fact]
    public void A_void_of_the_discounted_bill_gives_the_discount_up_and_the_food_rebills_at_the_orders_terms()
    {
        var (order, momo, coke) = Table();
        var billing = new Billing();
        var discounted = billing.Pay(order, PosOrderSplit.Items, Rupees(26m), items: [Of(momo, 1m), Of(coke, 1m)]);
        billing.Pay(order, PosOrderSplit.Items, items: [Of(momo, 1m)]);

        billing.Void(discounted);
        order.Reopen();

        // Nothing was written back: the discount was a figure of the voided bill, which now counts nowhere.
        Assert.Equal(0m, billing.Lines[momo.Id].PartDiscountAmount);
        var again = billing.Pay(order, PosOrderSplit.Whole);
        Assert.Equal(0m, again.PartDiscount);
        Assert.Equal(632.80m, billing.Totals.Unrounded);
        Assert.Equal(order.Estimate(billing.Lines).Total, billing.Totals.Unrounded);
    }

    [Fact]
    public void A_guests_discount_stands_beside_the_orders_percentage_and_its_amount_share()
    {
        // The order's 10% first, then the guest's 20 on what is left of their part.
        var (pct, pMomo, pCoke) = Table();
        pct.SetDiscount(PosDiscountKind.Percent, 10m, NothingBilled);
        var pctBilling = new Billing();
        var guest = pctBilling.Pay(pct, PosOrderSplit.Items, Rupees(20m), items: [Of(pMomo, 1m), Of(pCoke, 1m)]);

        // 180 and 54 after the order's 10%: 20 over them is 15.38 and 4.62.
        Assert.Equal([15.38m, 4.62m], guest.Lines.Select(x => x.PartDiscountShare));
        Assert.Equal([164.62m, 49.38m], guest.Lines.Select(x => x.Figures.Amount));

        // An order with an amount: its 52 is still spread over the dishes and every bill takes its share of it, the
        // guest's 26 is a second figure, and the order's shares still add up to the order's amount.
        var (amount, aMomo, aCoke) = Table("ORD0002");
        amount.SetDiscount(PosDiscountKind.Amount, 52m, NothingBilled);
        var amountBilling = new Billing();
        var first = amountBilling.Pay(amount, PosOrderSplit.Items, Rupees(26m), items: [Of(aMomo, 1m), Of(aCoke, 1m)]);
        var rest = amountBilling.Pay(amount, PosOrderSplit.Whole);

        Assert.Equal([20m, 6m], first.Lines.Select(x => x.BillDiscountShare));
        Assert.Equal([20m, 6m], rest.Lines.Select(x => x.BillDiscountShare));
        Assert.Equal(26m, first.PartDiscount);
        Assert.Equal(52m, amountBilling.Lines.Values.Sum(x => x.BillDiscountAmount));
        Assert.Equal(52m, amount.DiscountAmount);
    }

    [Fact]
    public void A_transfer_after_a_guests_discount_takes_only_the_orders_share()
    {
        var (source, momo, _) = Table(momo: 3m);
        source.SetDiscount(PosDiscountKind.Amount, 30m, NothingBilled);
        var billing = new Billing();
        billing.Pay(source, PosOrderSplit.Items, Rupees(40m), items: [Of(momo, 1m)]);

        // The momo's frozen share is 30 * 600 / 720 = 25: its first bill took 8.33 of it, and the guest's 40 beside it.
        Assert.Equal(25m, momo.OrderDiscountShare);
        Assert.Equal(8.33m, billing.Lines[momo.Id].BillDiscountAmount);

        var target = PosOrder.Open(
            Organization, Location, "ORD0002", PosTab.DineIn, Guid.NewGuid(), 1, null, User, DateTimeOffset.UtcNow);
        PosOrder.Transfer(source, target, [Of(momo, 2m)], User, billing.Lines, DateTimeOffset.UtcNow);

        // What is left of the share goes with the two momo; the guest's 40 stays on the bill it was given on.
        Assert.Equal(16.67m, target.DiscountAmount);
        Assert.Equal(30m - 16.67m, source.DiscountAmount);
    }

    [Fact]
    public void A_guests_discount_is_whole_paisa_a_real_percentage_and_never_more_than_their_part()
    {
        var (order, momo, coke) = Table();
        var billing = new Billing();
        PosOrderLineQuantity[] part = [Of(momo, 1m), Of(coke, 1m)];

        Assert.Throws<InvalidOperationException>(() => billing.Pay(order, PosOrderSplit.Items, Rupees(260.01m), items: part));
        Assert.Throws<InvalidOperationException>(() => billing.Pay(order, PosOrderSplit.Items, Rupees(-1m), items: part));
        Assert.Throws<InvalidOperationException>(() => billing.Pay(order, PosOrderSplit.Items, Rupees(1.005m), items: part));
        Assert.Throws<InvalidOperationException>(() => billing.Pay(order, PosOrderSplit.Items, Percent(100.01m), items: part));
        Assert.Throws<InvalidOperationException>(() => billing.Pay(order, PosOrderSplit.Items, Percent(10.001m), items: part));

        // All of it is a bill of nothing, which settles nothing and is still a bill (phase 72 Decision I).
        var free = billing.Pay(order, PosOrderSplit.Items, Percent(100m), items: part);
        Assert.Equal((0m, 0m), (free.Unrounded, free.Total));
    }

    [Fact]
    public void A_bill_stores_the_guests_discount_inside_its_bill_discount_beside_the_orders_percentage()
    {
        var invoice = Invoice.CreatePosSale(
            Organization, Guid.NewGuid(), Guid.NewGuid(), new DateOnly(2026, 10, 11), Location, Guid.NewGuid(),
            PosTab.DineIn, discountPct: 10m);
        invoice.BillPosOrder(Guid.NewGuid());
        var figures = new PosLineArithmetic.Figures(164.62m, 16.46m, 0m, 23.54m);

        // The order's percentage beside an amount share of the order's own is still refused (phase 74) ...
        Assert.Throws<InvalidOperationException>(() => invoice.AddPosOrderLine(
            Guid.NewGuid(), Guid.NewGuid(), 1m, 200m, VatRate.ThirteenPercentVat, 0m, 0m, 15.38m, 0m, null, 1m, 10m,
            PackagingFee.None, figures));

        // ... and a guest's discount is never more than the line's bill discount it is part of.
        var plain = Invoice.CreatePosSale(
            Organization, Guid.NewGuid(), Guid.NewGuid(), new DateOnly(2026, 10, 11), Location, Guid.NewGuid(), PosTab.DineIn);
        plain.BillPosOrder(Guid.NewGuid());
        Assert.Throws<InvalidOperationException>(() => plain.AddPosOrderLine(
            Guid.NewGuid(), Guid.NewGuid(), 1m, 200m, VatRate.ThirteenPercentVat, 0m, 0m, 15m, 15.38m, null, 1m, 10m,
            PackagingFee.None, figures));

        // ... but the guest's own beside it is the bill's term, not the order's.
        invoice.AddPosOrderLine(
            Guid.NewGuid(), Guid.NewGuid(), 1m, 200m, VatRate.ThirteenPercentVat, 0m, 0m, 15.38m, 15.38m, null, 1m, 10m,
            PackagingFee.None, figures);

        var line = Assert.Single(invoice.Lines);
        Assert.Equal((15.38m, 15.38m), (line.BillDiscountAmount, line.PartDiscountAmount));
        Assert.Equal((10m, 15.38m), (invoice.DiscountPct, invoice.DiscountAmount));
    }
}
