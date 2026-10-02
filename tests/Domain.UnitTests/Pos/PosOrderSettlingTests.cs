using ErpApp.Domain.Catalog;
using ErpApp.Domain.Pos;
using ErpApp.Domain.Sales;
using ErpApp.Domain.Tenancy;

namespace ErpApp.Domain.UnitTests.Pos;

/// <summary>
/// Phase 65 -- billing a restaurant order: the split planner, the order's settled state, and where each
/// kitchen ticket stands. Phase 59's service is the running example: 2 Chicken Momo (200, 10% service
/// charge) and 2 Coke 250ml (60, none), 13% VAT, which the vendor billed as 317 + 294 -- 20 of service
/// charge short (its defect 1). The whole bill is 632.80, rounded to 633.
/// </summary>
public class PosOrderSettlingTests
{
    private static readonly Guid User = Guid.NewGuid();

    private static PosOrderLineQuantity Q(PosOrderLine line, decimal quantity) => new(line.Id, quantity);

    /// <summary>What a test has billed so far: the planner's two inputs, kept the way the invoices would.</summary>
    private sealed class Billing
    {
        public Dictionary<Guid, PosOrderLineBilled> Lines { get; } = [];
        public PosOrderBilledTotals Totals { get; private set; } = PosOrderBilledTotals.None;
        public List<PosOrderBillPlan> Parts { get; } = [];

        public IReadOnlyDictionary<Guid, decimal> Invoiced => Lines.ToDictionary(x => x.Key, x => x.Value.Quantity);

        public PosOrderBillPlan Pay(PosOrder order, PosOrderSplit split, int parts = 0, params PosOrderLineQuantity[] items)
        {
            var plan = PosOrderBill.Plan(order, Lines, Totals, split, items, parts, roundOff: true);

            foreach (var line in plan.Lines)
            {
                var before = Lines.GetValueOrDefault(line.Line.Id);
                var figures = before is null ? line.Figures : new PosLineArithmetic.Figures(
                    before.Figures.Amount + line.Figures.Amount,
                    before.Figures.ServiceChargeAmount + line.Figures.ServiceChargeAmount,
                    before.Figures.VatAmount + line.Figures.VatAmount);
                Lines[line.Line.Id] = new PosOrderLineBilled((before?.Quantity ?? 0m) + line.Quantity, figures);
            }

            Totals = new PosOrderBilledTotals(Totals.Unrounded + plan.Unrounded, Totals.GrandTotal + plan.Total);
            Parts.Add(plan);
            order.SettleIfFullyBilled(Invoiced, DateTimeOffset.UtcNow);
            return plan;
        }
    }

    private static (PosOrder Order, PosOrderLine Momo, PosOrderLine Coke) PhaseFiftyNineTable()
    {
        var order = PosOrder.Open(
            Guid.NewGuid(), Guid.NewGuid(), "ORD0001", PosTab.DineIn, Guid.NewGuid(), 2, null, User, DateTimeOffset.UtcNow);
        order.Send(
            [
                new PosOrderNewLine(Guid.NewGuid(), null, 1m, 2m, 200m, VatRate.ThirteenPercentVat, 10m, null, null),
                new PosOrderNewLine(Guid.NewGuid(), null, 1m, 2m, 60m, VatRate.ThirteenPercentVat, 0m, null, null),
            ],
            [], User);
        return (order, order.Lines[0], order.Lines[1]);
    }

    [Fact]
    public void The_whole_order_on_one_bill_is_the_estimate_rounded()
    {
        var (order, _, _) = PhaseFiftyNineTable();

        var plan = new Billing().Pay(order, PosOrderSplit.Whole);

        Assert.Equal(632.80m, plan.Unrounded);
        Assert.Equal(0.20m, plan.RoundOff);
        Assert.Equal(633m, plan.Total);
        Assert.Equal(40m, plan.ServiceCharge);
        Assert.True(plan.BillsTheRest);
        Assert.Equal(PosOrderStatus.Settled, order.Status);
    }

    /// <summary>The vendor's defect 1, as a regression test: its remainder was billed 294 with no service
    /// charge. A split moves quantities and never rates, so both halves carry 20 and the table pays 40.</summary>
    [Fact]
    public void A_split_by_item_keeps_the_service_charge_on_both_bills_and_they_add_up_to_the_order()
    {
        var (order, momo, coke) = PhaseFiftyNineTable();
        var billing = new Billing();

        var first = billing.Pay(order, PosOrderSplit.Items, 0, Q(momo, 1m), Q(coke, 1m));

        Assert.Equal(316.40m, first.Unrounded);
        Assert.Equal(316m, first.Total);
        Assert.Equal(20m, first.ServiceCharge);
        Assert.False(first.BillsTheRest);
        Assert.Equal(633m, first.OrderTotal);
        Assert.Equal(317m, first.LeftAfter);
        Assert.Equal(PosOrderStatus.Open, order.Status);

        var rest = billing.Pay(order, PosOrderSplit.Whole);

        Assert.Equal(20m, rest.ServiceCharge);
        Assert.Equal(316.40m, rest.Unrounded);
        Assert.Equal(317m, rest.Total);
        Assert.Equal(0.60m, rest.RoundOff);
        Assert.True(rest.BillsTheRest);

        Assert.Equal(40m, billing.Parts.Sum(x => x.ServiceCharge));
        Assert.Equal(633m, billing.Parts.Sum(x => x.Total));
        Assert.Equal(PosOrderStatus.Settled, order.Status);
        Assert.NotNull(order.SettledAt);
    }

    [Fact]
    public void An_equal_split_divides_every_line_and_the_last_part_takes_what_is_left()
    {
        var (order, momo, coke) = PhaseFiftyNineTable();
        var billing = new Billing();

        var first = billing.Pay(order, PosOrderSplit.Equal, parts: 2);

        Assert.Equal([1m, 1m], first.Lines.Select(x => x.Quantity));
        Assert.Equal(316m, first.Total);

        var second = billing.Pay(order, PosOrderSplit.Equal, parts: 1);

        Assert.Equal(317m, second.Total);
        Assert.Equal(633m, billing.Totals.GrandTotal);
        Assert.Equal(PosOrderStatus.Settled, order.Status);
        Assert.Equal(2m, billing.Invoiced[momo.Id]);
        Assert.Equal(2m, billing.Invoiced[coke.Id]);
    }

    /// <summary>One Thukpa between three: fractional lines, each money figure of the line adding up across
    /// the parts to what the whole line comes to, and every bill a whole rupee.</summary>
    [Fact]
    public void An_equal_split_of_one_dish_in_three_ties_out_to_the_paisa()
    {
        var order = PosOrder.Open(
            Guid.NewGuid(), Guid.NewGuid(), "ORD0002", PosTab.DineIn, Guid.NewGuid(), 3, null, User, DateTimeOffset.UtcNow);
        order.Send([new PosOrderNewLine(Guid.NewGuid(), null, 1m, 1m, 180m, VatRate.ThirteenPercentVat, 10m, null, null)], [], User);
        var thukpa = order.Lines[0];
        var whole = thukpa.Figures(1m);
        var billing = new Billing();

        billing.Pay(order, PosOrderSplit.Equal, parts: 3);
        billing.Pay(order, PosOrderSplit.Equal, parts: 2);
        billing.Pay(order, PosOrderSplit.Equal, parts: 1);

        Assert.Equal([0.3333m, 0.3334m, 0.3333m], billing.Parts.Select(x => x.Lines.Single().Quantity));
        Assert.Equal(whole.Amount, billing.Lines[thukpa.Id].Figures.Amount);
        Assert.Equal(whole.ServiceChargeAmount, billing.Lines[thukpa.Id].Figures.ServiceChargeAmount);
        Assert.Equal(whole.VatAmount, billing.Lines[thukpa.Id].Figures.VatAmount);
        Assert.All(billing.Parts, x => Assert.Equal(decimal.Round(x.Total, 0), x.Total));
        Assert.All(billing.Parts, x => Assert.True(Math.Abs(x.RoundOff) < 1m));
        Assert.Equal(decimal.Round(whole.Amount + whole.ServiceChargeAmount + whole.VatAmount, 0, MidpointRounding.AwayFromZero),
            billing.Totals.GrandTotal);
    }

    /// <summary>Rounding each part on its own would bill 11 + 11 + 0 for 21.20: the running total keeps
    /// the parts at the order's 21 and every round-off under a rupee.</summary>
    [Fact]
    public void Parts_round_on_the_running_total_never_on_their_own()
    {
        var order = PosOrder.Open(
            Guid.NewGuid(), Guid.NewGuid(), "ORD0003", PosTab.TakeAway, null, 0, null, User, DateTimeOffset.UtcNow);
        order.Send(
            [
                new PosOrderNewLine(Guid.NewGuid(), null, 1m, 1m, 10.50m, VatRate.NoVat, 0m, null, null),
                new PosOrderNewLine(Guid.NewGuid(), null, 1m, 1m, 10.50m, VatRate.NoVat, 0m, null, null),
                new PosOrderNewLine(Guid.NewGuid(), null, 1m, 1m, 0.20m, VatRate.NoVat, 0m, null, null),
            ],
            [], User);
        var billing = new Billing();

        billing.Pay(order, PosOrderSplit.Items, 0, Q(order.Lines[0], 1m));
        billing.Pay(order, PosOrderSplit.Items, 0, Q(order.Lines[1], 1m));
        billing.Pay(order, PosOrderSplit.Items, 0, Q(order.Lines[2], 1m));

        Assert.Equal([11m, 10m, 0m], billing.Parts.Select(x => x.Total));
        Assert.Equal(21m, billing.Totals.GrandTotal);
        Assert.All(billing.Parts, x => Assert.True(Math.Abs(x.RoundOff) < 1m));
    }

    [Fact]
    public void A_part_cannot_bill_more_than_is_left_or_name_a_line_twice_and_a_settled_order_has_nothing_to_bill()
    {
        var (order, momo, _) = PhaseFiftyNineTable();
        var billing = new Billing();

        Assert.Throws<InvalidOperationException>(() => billing.Pay(order, PosOrderSplit.Items, 0, Q(momo, 3m)));
        Assert.Throws<InvalidOperationException>(() =>
            billing.Pay(order, PosOrderSplit.Items, 0, Q(momo, 1m), Q(momo, 1m)));
        Assert.Throws<InvalidOperationException>(() => billing.Pay(order, PosOrderSplit.Items, 0));
        Assert.Throws<InvalidOperationException>(() => billing.Pay(order, PosOrderSplit.Equal, parts: 0));

        billing.Pay(order, PosOrderSplit.Items, 0, Q(momo, 2m));
        Assert.Throws<InvalidOperationException>(() => billing.Pay(order, PosOrderSplit.Items, 0, Q(momo, 1m)));

        billing.Pay(order, PosOrderSplit.Whole);
        Assert.Equal(PosOrderStatus.Settled, order.Status);
        Assert.Throws<InvalidOperationException>(() => billing.Pay(order, PosOrderSplit.Whole));
    }

    [Fact]
    public void What_is_billed_cannot_be_discarded_and_a_part_billed_order_cannot_be_voided()
    {
        var (order, momo, coke) = PhaseFiftyNineTable();
        var billing = new Billing();
        billing.Pay(order, PosOrderSplit.Items, 0, Q(momo, 1m));

        Assert.Throws<InvalidOperationException>(() =>
            order.Discard([new(momo.Id, 2m)], "typo", User, billing.Invoiced));
        Assert.Throws<InvalidOperationException>(() =>
            order.Void("Guests left", User, DateTimeOffset.UtcNow, billing.Invoiced));

        // Discarding the unbilled rest settles it: nothing is left, and something was billed.
        order.Discard([new(momo.Id, 1m), new(coke.Id, 2m)], "Guests left", User, billing.Invoiced);
        Assert.True(order.SettleIfFullyBilled(billing.Invoiced, DateTimeOffset.UtcNow));
        Assert.Equal(PosOrderStatus.Settled, order.Status);
    }

    [Fact]
    public void An_order_with_nothing_billed_stays_open_and_a_reopened_order_can_be_billed_again()
    {
        var (order, momo, coke) = PhaseFiftyNineTable();
        Assert.False(order.SettleIfFullyBilled(new Dictionary<Guid, decimal>(), DateTimeOffset.UtcNow));

        var billing = new Billing();
        billing.Pay(order, PosOrderSplit.Whole);
        Assert.Equal(PosOrderStatus.Settled, order.Status);

        // A settled order can still be served -- a Take Away is paid before it is packed -- but not added to.
        order.Serve([new(momo.Id, 2m)]);
        Assert.Throws<InvalidOperationException>(() => order.Send([], [new(coke.Id, 1m)], User));

        // Its invoice voided: the quantities are back, and so is the order.
        order.Reopen();
        Assert.Equal(PosOrderStatus.Open, order.Status);
        Assert.Null(order.SettledAt);
        var again = PosOrderBill.Plan(
            order, new Dictionary<Guid, PosOrderLineBilled>(), PosOrderBilledTotals.None, PosOrderSplit.Whole, [], 0, true);
        Assert.Equal(633m, again.Total);
    }

    [Fact]
    public void Without_rounding_a_part_is_its_own_figures()
    {
        var (order, momo, _) = PhaseFiftyNineTable();

        var plan = PosOrderBill.Plan(
            order, new Dictionary<Guid, PosOrderLineBilled>(), PosOrderBilledTotals.None, PosOrderSplit.Items,
            [new(momo.Id, 1m)], 0, roundOff: false);

        Assert.Equal(248.60m, plan.Total);
        Assert.Equal(0m, plan.RoundOff);
        Assert.Equal(632.80m, plan.OrderTotal);
    }

    /// <summary>The board's view: served goes to the earliest sends, a discard to the latest unserved.</summary>
    [Fact]
    public void Each_send_ticket_shows_what_is_still_to_cook()
    {
        var order = PosOrder.Open(
            Guid.NewGuid(), Guid.NewGuid(), "ORD0004", PosTab.DineIn, Guid.NewGuid(), 2, null, User, DateTimeOffset.UtcNow);
        order.Send([new PosOrderNewLine(Guid.NewGuid(), null, 1m, 2m, 200m, VatRate.ThirteenPercentVat, 10m, null, null)], [], User);
        var momo = order.Lines[0];
        order.Send([], [new(momo.Id, 1m)], User);

        var progress = order.TicketProgress();
        Assert.Equal([KitchenTicketState.Pending, KitchenTicketState.Pending], progress.Select(x => x.State));

        order.Serve([new(momo.Id, 2m)]);
        progress = order.TicketProgress();
        Assert.Equal(KitchenTicketState.Served, progress[0].State);
        Assert.Equal(KitchenTicketState.Pending, progress[1].State);
        Assert.Equal(1m, progress[1].Lines.Single().Pending);

        order.Discard([new(momo.Id, 1m)], "Guest changed their mind", User, new Dictionary<Guid, decimal>());
        progress = order.TicketProgress();
        Assert.Equal(
            [KitchenTicketState.Served, KitchenTicketState.Cancelled, KitchenTicketState.Cancellation],
            progress.Select(x => x.State));
        Assert.Equal(1m, progress[1].Lines.Single().Cancelled);
    }

    [Fact]
    public void A_bill_for_an_order_names_it_first_carries_no_discount_and_rounds_by_under_a_rupee()
    {
        var invoice = Invoice.CreatePosSale(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), new DateOnly(2026, 10, 2), Guid.NewGuid(), Guid.NewGuid(),
            PosTab.DineIn);
        var figures = new PosLineArithmetic.Figures(200m, 20m, 28.60m);

        Assert.Throws<InvalidOperationException>(() => invoice.AddPosOrderLine(
            Guid.NewGuid(), Guid.NewGuid(), 1m, 200m, VatRate.ThirteenPercentVat, null, 1m, 10m, figures));

        invoice.BillPosOrder(Guid.NewGuid());
        Assert.Throws<InvalidOperationException>(() => invoice.BillPosOrder(Guid.NewGuid()));
        Assert.Throws<InvalidOperationException>(() => invoice.AddPosOrderLine(
            Guid.NewGuid(), Guid.NewGuid(), 1m, 200m, VatRate.ThirteenPercentVat, null, 1m, 10m,
            new PosLineArithmetic.Figures(200.001m, 20m, 28.60m)));

        invoice.AddPosOrderLine(Guid.NewGuid(), Guid.NewGuid(), 1m, 200m, VatRate.ThirteenPercentVat, null, 1m, 10m, figures);
        Assert.Equal(248.60m, invoice.GrandTotal);
        Assert.Equal(20m, invoice.ServiceChargeTotal);

        Assert.Throws<InvalidOperationException>(() => invoice.SetPosRoundOff(1m));
        invoice.SetPosRoundOff(0.40m);
        Assert.Equal(249m, invoice.GrandTotal);
    }
}
