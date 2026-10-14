using ErpApp.Domain.Catalog;
using ErpApp.Domain.Pos;
using ErpApp.Domain.Sales;
using ErpApp.Domain.Tenancy;

namespace ErpApp.Domain.UnitTests.Pos;

/// <summary>
/// Phase 91 -- one guest declining the service charge on their own part (phase 83 § 5, Decision J there; Decision E
/// here). The part carries none and records what it declined; the order's terms stay as they are. The rule every test
/// comes back to is phase 83's twin tab: every other guest's bill is what it would have been had the first guest paid
/// the service charge, to the paisa, and a void gives the waiver up with nothing written back.
/// </summary>
public class PosPartServiceChargeWaiverTests
{
    private static readonly Guid User = Guid.NewGuid();

    /// <summary>Three momo at 200 with 10% service charge and two Coke at 60 with none, 13% VAT, dine in.</summary>
    private static (PosOrder Order, PosOrderLine Momo, PosOrderLine Coke) Table(string code = "ORD0001")
    {
        var order = PosOrder.Open(
            Guid.NewGuid(), Guid.NewGuid(), code, PosTab.DineIn, Guid.NewGuid(), 3, null, User, DateTimeOffset.UtcNow);
        order.Send(
            [
                new PosOrderNewLine(Guid.NewGuid(), null, 1m, 3m, 200m, VatRate.ThirteenPercentVat, 10m, PackagingFee.None, null, null),
                new PosOrderNewLine(Guid.NewGuid(), null, 1m, 2m, 60m, VatRate.ThirteenPercentVat, 0m, PackagingFee.None, null, null),
            ],
            [], User);
        return (order, order.Lines[0], order.Lines[1]);
    }

    /// <summary>What a test has billed, kept as the reader keeps it (<c>PosOrderBilling</c>): sums over bills not voided,
    /// the waiver a figure of its own.</summary>
    private sealed class Billing
    {
        public Dictionary<Guid, PosOrderLineBilled> Lines { get; } = [];
        public PosOrderBilledTotals Totals { get; private set; } = PosOrderBilledTotals.None;
        private readonly List<PosOrderBillPlan> _bills = [];

        public PosOrderBillPlan Pay(
            PosOrder order, PosOrderSplit split, bool waive = false, PosPartDiscount? discount = null, int parts = 0,
            params PosOrderLineQuantity[] items)
        {
            var plan = PosOrderBill.Plan(order, Lines, Totals, split, items, parts, roundOff: true, discount, waive);
            _bills.Add(plan);
            Rebuild();
            order.FixDiscountShares();
            return plan;
        }

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
                    (before?.PartDiscountAmount ?? 0m) + line.PartDiscountShare,
                    (before?.PartServiceChargeWaived ?? 0m) + line.ServiceChargeWaived);
            }

            Totals = new PosOrderBilledTotals(_bills.Sum(x => x.Unrounded), _bills.Sum(x => x.Total));
        }
    }

    private static PosOrderLineQuantity Of(PosOrderLine line, decimal quantity) => new(line.Id, quantity);

    [Fact]
    public void A_guest_declining_it_pays_no_service_charge_and_no_VAT_on_it_and_the_part_records_what_was_declined()
    {
        var (order, momo, coke) = Table();
        var billing = new Billing();

        var first = billing.Pay(order, PosOrderSplit.Items, waive: true, items: [Of(momo, 1m), Of(coke, 1m)]);

        // A momo would have been 200 + 20 + 28.60; declined, 200 + 26. The Coke had none to decline.
        Assert.Equal(
            [new PosLineArithmetic.Figures(200m, 0m, 0m, 26m), new PosLineArithmetic.Figures(60m, 0m, 0m, 7.80m)],
            first.Lines.Select(x => x.Figures));
        Assert.Equal([20m, 0m], first.Lines.Select(x => x.ServiceChargeWaived));
        Assert.Equal(20m, first.ServiceChargeWaived);
        Assert.Equal((293.80m, 294m), (first.Unrounded, first.Total));

        // The order's own terms did not move: its waiver is still off.
        Assert.False(order.ServiceChargeWaived);
    }

    [Fact]
    public void The_next_guests_bill_is_what_it_would_have_been_had_the_first_guest_paid_the_service_charge()
    {
        var (waivedTab, wMomo, _) = Table();
        var waiving = new Billing();
        waiving.Pay(waivedTab, PosOrderSplit.Items, waive: true, items: [Of(wMomo, 1m)]);
        var nextAfterWaiver = waiving.Pay(waivedTab, PosOrderSplit.Whole);

        var (plainTab, pMomo, _) = Table("ORD0002");
        var plain = new Billing();
        plain.Pay(plainTab, PosOrderSplit.Items, items: [Of(pMomo, 1m)]);
        var nextAfterPaying = plain.Pay(plainTab, PosOrderSplit.Whole);

        Assert.Equal(nextAfterPaying.Lines.Select(x => x.Figures), nextAfterWaiver.Lines.Select(x => x.Figures));
        Assert.Equal(nextAfterPaying.Unrounded, nextAfterWaiver.Unrounded);

        // The two momo left carry 40 of service charge -- the line's 60 less the 20 declined -- not the 60 a last part
        // pricing "what is left" from the whole line would otherwise charge them.
        Assert.Equal(40m, nextAfterWaiver.Lines.Single(x => x.Line.Id == wMomo.Id).Figures.ServiceChargeAmount);
    }

    [Fact]
    public void The_last_part_declines_what_is_left_of_the_lines_service_charge()
    {
        var (order, momo, _) = Table();
        var billing = new Billing();

        billing.Pay(order, PosOrderSplit.Items, items: [Of(momo, 1m)]);
        var last = billing.Pay(order, PosOrderSplit.Whole, waive: true);

        var lastMomo = last.Lines.Single(x => x.Line.Id == momo.Id);
        Assert.Equal(new PosLineArithmetic.Figures(400m, 0m, 0m, 52m), lastMomo.Figures);
        Assert.Equal(40m, lastMomo.ServiceChargeWaived);
        Assert.True(last.BillsTheRest);
    }

    [Fact]
    public void A_void_gives_the_waiver_up_and_the_rest_of_the_line_is_charged_in_full_again()
    {
        var (order, momo, _) = Table();
        var billing = new Billing();

        var waived = billing.Pay(order, PosOrderSplit.Items, waive: true, items: [Of(momo, 1m)]);
        billing.Void(waived);
        var whole = billing.Pay(order, PosOrderSplit.Whole);

        Assert.Equal(new PosLineArithmetic.Figures(600m, 60m, 0m, 85.80m), whole.Lines.Single(x => x.Line.Id == momo.Id).Figures);
    }

    [Fact]
    public void The_order_comes_to_its_bills_once_a_guest_has_declined()
    {
        var (order, momo, _) = Table();
        var billing = new Billing();

        var first = billing.Pay(order, PosOrderSplit.Items, waive: true, items: [Of(momo, 1m)]);
        var rest = billing.Pay(order, PosOrderSplit.Whole);

        // 600 + 120 of food, 60 of service charge, VAT on all of it -- less the 20 declined and its 2.60 of VAT.
        Assert.Equal(first.Total + rest.Total, rest.OrderTotal);
        Assert.Equal(order.Estimate(billing.Lines).Total, first.Unrounded + rest.Unrounded);
        Assert.Equal(20m, billing.Lines[momo.Id].PartServiceChargeWaived);
    }

    [Fact]
    public void A_guests_own_discount_comes_first_and_the_declined_service_charge_is_on_what_is_left()
    {
        var (order, momo, _) = Table();
        var billing = new Billing();

        var first = billing.Pay(
            order, PosOrderSplit.Items, waive: true, discount: new PosPartDiscount(PosDiscountKind.Amount, 20m),
            items: [Of(momo, 1m)]);

        var line = first.Lines.Single();
        Assert.Equal(new PosLineArithmetic.Figures(180m, 0m, 0m, 23.40m), line.Figures);
        Assert.Equal((20m, 18m), (line.PartDiscountShare, line.ServiceChargeWaived));
    }

    [Fact]
    public void There_is_nothing_to_decline_where_the_order_already_waived_it()
    {
        var (order, momo, _) = Table();
        order.WaiveServiceCharge(true, new Dictionary<Guid, decimal>());
        var billing = new Billing();

        var first = billing.Pay(order, PosOrderSplit.Items, waive: true, items: [Of(momo, 1m)]);

        Assert.Equal(0m, first.ServiceChargeWaived);
        Assert.Equal(new PosLineArithmetic.Figures(200m, 0m, 0m, 26m), first.Lines.Single().Figures);
    }

    [Fact]
    public void Declining_takes_the_service_charge_off_and_VAT_again_and_never_more_than_there_is()
    {
        var figures = new PosLineArithmetic.Figures(200m, 20m, 0m, 28.60m);

        Assert.Equal(new PosLineArithmetic.Figures(200m, 5m, 0m, 26.65m), figures.LessServiceCharge(15m, 0.13m));
        Assert.Equal(figures, figures.LessServiceCharge(0m, 0.13m));
        Assert.Throws<InvalidOperationException>(() => figures.LessServiceCharge(20.01m, 0.13m));
        Assert.Throws<InvalidOperationException>(() => figures.LessServiceCharge(0.001m, 0.13m));
    }
}
