using ErpApp.Domain.Catalog;
using ErpApp.Domain.Pos;
using ErpApp.Domain.Tenancy;

namespace ErpApp.Domain.UnitTests.Pos;

/// <summary>
/// Phase 64 -- the restaurant's Domain: an order whose quantities are sums over its kitchen tickets, the
/// floor, kitchen stations, and the dine-in-only service charge. Phase 59's service is the running
/// example: Chicken Momo (200, service charge, the Kitchen) and Coke 250ml (60, no service charge,
/// Default).
/// </summary>
public class PosRestaurantTests
{
    private static readonly Guid Organization = Guid.NewGuid();
    private static readonly Guid Location = Guid.NewGuid();
    private static readonly Guid User = Guid.NewGuid();
    private static readonly Guid Kitchen = Guid.NewGuid();
    private static readonly Guid Bar = Guid.NewGuid();

    private static PosOrder DineIn() => PosOrder.Open(
        Organization, Location, "ORD0001", PosTab.DineIn, Guid.NewGuid(), covers: 2, contactId: null, User,
        DateTimeOffset.UtcNow);

    private static PosOrderNewLine Momo(decimal quantity, string? note = null, Guid? station = null) => new(
        Guid.NewGuid(), Guid.NewGuid(), 1m, quantity, 200m, VatRate.ThirteenPercentVat, 10m, note, station ?? Kitchen);

    private static PosOrderNewLine Coke(decimal quantity) => new(
        Guid.NewGuid(), Guid.NewGuid(), 1m, quantity, 60m, VatRate.ThirteenPercentVat, 0m, null, null);

    private static PosOrderLineQuantity Of(PosOrderLine line, decimal quantity) => new(line.Id, quantity);

    private static readonly IReadOnlyDictionary<Guid, decimal> NothingBilled = new Dictionary<Guid, decimal>();

    [Fact]
    public void A_dine_in_order_is_seated_at_a_table_with_guests_and_the_other_two_types_are_not()
    {
        Assert.Throws<InvalidOperationException>(() => PosOrder.Open(
            Organization, Location, "ORD1", PosTab.DineIn, null, 2, null, User, DateTimeOffset.UtcNow));
        Assert.Throws<InvalidOperationException>(() => PosOrder.Open(
            Organization, Location, "ORD1", PosTab.DineIn, Guid.NewGuid(), 0, null, User, DateTimeOffset.UtcNow));
        Assert.Throws<InvalidOperationException>(() => PosOrder.Open(
            Organization, Location, "ORD1", PosTab.TakeAway, Guid.NewGuid(), 0, null, User, DateTimeOffset.UtcNow));

        // A Delivery goes to somebody; a Retail hold is never an order (phase 62 Decision C).
        Assert.Throws<InvalidOperationException>(() => PosOrder.Open(
            Organization, Location, "ORD1", PosTab.Delivery, null, 0, null, User, DateTimeOffset.UtcNow));
        Assert.Throws<InvalidOperationException>(() => PosOrder.Open(
            Organization, Location, "ORD1", PosTab.Retail, null, 0, null, User, DateTimeOffset.UtcNow));

        var takeAway = PosOrder.Open(Organization, Location, "ORD1", PosTab.TakeAway, null, 0, null, User, DateTimeOffset.UtcNow);
        Assert.Equal(PosOrderStatus.Open, takeAway.Status);
    }

    [Fact]
    public void An_order_is_dated_by_the_Nepal_day_it_was_opened()
    {
        // 18:30 UTC is 00:15 the next morning in Kathmandu (phase 20e's after-local-midnight case).
        var lateEvening = new DateTimeOffset(2026, 10, 1, 18, 30, 0, TimeSpan.Zero);
        var order = PosOrder.Open(Organization, Location, "ORD1", PosTab.TakeAway, null, 0, null, User, lateEvening);

        Assert.Equal(new DateOnly(2026, 10, 2), order.Date);
    }

    [Fact]
    public void The_first_send_is_one_ticket_per_station_with_the_Default_station_last()
    {
        var order = DineIn();

        var result = order.Send([Coke(1), Momo(2, "less spicy")], [], User);

        Assert.Equal(2, result.NewLines.Count);
        Assert.Equal(2, result.NewTickets.Count);
        Assert.All(result.NewTickets, t => Assert.Equal(1, t.SendNumber));
        Assert.Equal(Kitchen, result.NewTickets[0].KitchenStationId);
        Assert.Null(result.NewTickets[1].KitchenStationId);
        Assert.All(result.NewTickets, t => Assert.False(t.IsCancellation));

        var momo = order.Lines.Single(x => x.Rate == 200m);
        Assert.Equal("less spicy", momo.Note);
        Assert.Equal(new PosOrderLineQuantities(2m, 0m, 2m, 0m), order.QuantitiesOf(momo));
    }

    [Fact]
    public void Adding_to_a_sent_line_tickets_only_the_change_to_the_station_it_was_first_sent_to()
    {
        // Phase 59's service: 2 Momo + 1 Coke, then 1 more Coke -- "a third KOT carrying only the delta".
        var order = DineIn();
        order.Send([Momo(2), Coke(1)], [], User);
        var coke = order.Lines.Single(x => x.Rate == 60m);

        var result = order.Send([], [Of(coke, 1m)], User);

        var ticket = Assert.Single(result.NewTickets);
        Assert.Equal(2, ticket.SendNumber);
        Assert.Null(ticket.KitchenStationId);
        var row = Assert.Single(ticket.Lines);
        Assert.Equal(1m, row.Quantity);
        Assert.Empty(result.NewLines);
        Assert.Equal(2m, order.QuantitiesOf(coke).Net);
        Assert.Equal(3, order.Tickets.Count);
    }

    [Fact]
    public void Serving_is_partial_by_quantity_and_never_more_than_is_outstanding()
    {
        var order = DineIn();
        order.Send([Momo(2)], [], User);
        var momo = order.Lines.Single();

        order.Serve([Of(momo, 1m)]);
        Assert.Equal(1m, order.QuantitiesOf(momo).Served);
        Assert.Equal(1m, order.QuantitiesOf(momo).Outstanding);

        Assert.Throws<InvalidOperationException>(() => order.Serve([Of(momo, 2m)]));
        order.Serve([Of(momo, 1m)]);
        Assert.Equal(0m, order.QuantitiesOf(momo).Outstanding);
    }

    [Fact]
    public void A_discard_needs_a_reason_and_reaches_the_kitchen_as_a_negative_ticket()
    {
        var order = DineIn();
        order.Send([Momo(2)], [], User);
        var momo = order.Lines.Single();

        Assert.Throws<InvalidOperationException>(() => order.Discard([Of(momo, 1m)], "  ", User, NothingBilled));

        var ticket = Assert.Single(order.Discard([Of(momo, 1m)], "Guest changed their mind", User, NothingBilled));

        Assert.True(ticket.IsCancellation);
        Assert.Equal("Guest changed their mind", ticket.Reason);
        Assert.Equal(Kitchen, ticket.KitchenStationId);
        Assert.Equal(-1m, Assert.Single(ticket.Lines).Quantity);
        Assert.Equal(new PosOrderLineQuantities(2m, 1m, 1m, 0m), order.QuantitiesOf(momo));
    }

    [Fact]
    public void A_discard_cannot_take_back_more_than_is_on_the_order()
    {
        var order = DineIn();
        order.Send([Momo(2)], [], User);
        var momo = order.Lines.Single();

        Assert.Throws<InvalidOperationException>(() => order.Discard([Of(momo, 3m)], "typo", User, NothingBilled));
        order.Discard([Of(momo, 2m)], "typo", User, NothingBilled);
        Assert.Throws<InvalidOperationException>(() => order.Discard([Of(momo, 1m)], "again", User, NothingBilled));
    }

    [Fact]
    public void A_dish_sent_back_after_serving_brings_the_served_count_down_with_it()
    {
        // Unserved quantity is cancelled first; only beyond it does served fall, so it never exceeds net.
        var order = DineIn();
        order.Send([Momo(3)], [], User);
        var momo = order.Lines.Single();
        order.Serve([Of(momo, 2m)]);

        order.Discard([Of(momo, 1m)], "never cooked", User, NothingBilled);
        Assert.Equal(new PosOrderLineQuantities(3m, 1m, 2m, 2m), order.QuantitiesOf(momo));

        order.Discard([Of(momo, 1m)], "sent back cold", User, NothingBilled);
        Assert.Equal(new PosOrderLineQuantities(3m, 2m, 1m, 1m), order.QuantitiesOf(momo));
        Assert.Equal(0m, order.QuantitiesOf(momo).Outstanding);
    }

    [Fact]
    public void Voiding_an_order_cancels_what_is_left_to_the_kitchen_and_closes_it()
    {
        var order = DineIn();
        order.Send([Momo(2), Coke(1)], [], User);
        var momo = order.Lines.Single(x => x.Rate == 200m);
        order.Discard([Of(momo, 1m)], "typo", User, NothingBilled);

        var tickets = order.Void("Guests left", User, DateTimeOffset.UtcNow, NothingBilled);

        Assert.Equal(2, tickets.Count);
        Assert.All(tickets, t => Assert.True(t.IsCancellation));
        Assert.All(order.Lines, l => Assert.Equal(0m, order.QuantitiesOf(l).Net));
        Assert.Equal(PosOrderStatus.Voided, order.Status);
        Assert.Equal("Guests left", order.VoidReason);
        Assert.Throws<InvalidOperationException>(() => order.Send([Coke(1)], [], User));
        Assert.Throws<InvalidOperationException>(() => order.Void("again", User, DateTimeOffset.UtcNow, NothingBilled));
    }

    [Fact]
    public void A_ticket_counts_its_prints_so_the_second_one_is_a_reprint()
    {
        var order = DineIn();
        var ticket = order.Send([Momo(1)], [], User).NewTickets.Single();

        Assert.Equal(1, order.RecordTicketPrint(ticket.Id));
        Assert.Equal(2, order.RecordTicketPrint(ticket.Id));
        Assert.Throws<InvalidOperationException>(() => order.RecordTicketPrint(Guid.NewGuid()));
    }

    [Fact]
    public void The_estimate_prices_each_line_by_the_tills_one_arithmetic()
    {
        // The vendor's own line (erp-module-scan.md): 2 Momo -> sub_total 400, service charge 40,
        // taxable 440, VAT 57.2, grand total 497.2.
        var order = DineIn();
        order.Send([Momo(2)], [], User);

        var estimate = order.Estimate();

        Assert.Equal(400m, estimate.Amount);
        Assert.Equal(40m, estimate.ServiceChargeAmount);
        Assert.Equal(57.20m, estimate.VatAmount);
    }

    [Fact]
    public void A_dine_in_order_moves_tables_and_the_others_have_none_to_move()
    {
        var order = DineIn();
        var table = Guid.NewGuid();
        order.MoveToTable(table);
        Assert.Equal(table, order.PosTableId);

        var takeAway = PosOrder.Open(Organization, Location, "ORD2", PosTab.TakeAway, null, 0, null, User, DateTimeOffset.UtcNow);
        Assert.Throws<InvalidOperationException>(() => takeAway.MoveToTable(table));
    }

    [Theory]
    [InlineData(PosTab.DineIn, 10)]
    [InlineData(PosTab.TakeAway, 0)]
    [InlineData(PosTab.Delivery, 0)]
    [InlineData(PosTab.Retail, 10)]
    [InlineData(null, 10)]
    public void Service_charge_is_a_dine_in_charge(PosTab? orderType, int expected)
    {
        var settings = PosLocationSettings.CreateDefault(Organization, Location);
        settings.Update(
            PosMode.Restaurant, serviceChargeEnabled: true, serviceChargeRate: 10m, serviceChargeAccountId: null,
            serviceChargeOnTakeAway: true, roundOffEnabled: false, roundOffAccountId: null, cashVerificationRequired: false,
            denominations: PosLocationSettings.DefaultDenominations, defaultTab: null, printEstimateBill: true,
            printInvoice: true, printCreditNote: true, printKot: true, abbreviatedTaxInvoiceEnabled: false);

        Assert.Equal(expected, PosServiceCharge.RateFor(settings, productApplicable: true, orderType));
        Assert.Equal(0m, PosServiceCharge.RateFor(settings, productApplicable: false, orderType));
        Assert.Equal(0m, PosServiceCharge.RateFor(
            PosLocationSettings.CreateDefault(Organization, Location), productApplicable: true, orderType));
    }

    private static PosTableLayout Table(string name, Guid? id = null, int x = 10, bool active = true) =>
        new(id, name, 4, PosTableShape.Rectangle, x, 10, 250, 100, active);

    [Fact]
    public void A_layout_adds_new_tables_and_must_name_every_table_already_on_the_area()
    {
        var area = PosArea.Create(Organization, Location, "Ground Floor");
        var added = area.ApplyLayout([Table("T1"), Table("T2", x: 300)]);
        Assert.Equal(2, added.Count);

        var t1 = added[0];
        Assert.Throws<InvalidOperationException>(() => area.ApplyLayout([Table("T1", t1.Id)]));

        var again = area.ApplyLayout([Table("T1", t1.Id, x: 500), Table("T2", added[1].Id, active: false), Table("T3", x: 800)]);
        Assert.Single(again);
        Assert.Equal(500, t1.X);
        Assert.False(added[1].IsActive);
        Assert.Equal(3, area.Tables.Count);
    }

    [Fact]
    public void A_layout_refuses_two_tables_of_one_name_and_a_table_off_the_floor()
    {
        var area = PosArea.Create(Organization, Location, "Ground Floor");

        Assert.Throws<InvalidOperationException>(() => area.ApplyLayout([Table("T1"), Table("t1", x: 300)]));
        Assert.Throws<InvalidOperationException>(() => area.ApplyLayout([Table("T1", x: PosArea.CanvasWidth - 100)]));
        Assert.Throws<InvalidOperationException>(() => area.ApplyLayout(
            [new PosTableLayout(null, "T1", PosTable.MaxCapacity + 1, PosTableShape.Circle, 0, 0, 100, 100, true)]));
        Assert.Throws<InvalidOperationException>(() => area.ApplyLayout(
            [new PosTableLayout(null, "T1", 4, PosTableShape.Circle, 0, 0, PosTable.MinSize - 1, 100, true)]));
    }

    [Fact]
    public void A_kitchen_station_cannot_be_called_Default_or_be_deactivated_while_products_go_there()
    {
        Assert.Throws<InvalidOperationException>(() => KitchenStation.Create(Organization, "default"));

        var bar = KitchenStation.Create(Organization, "Bar");
        Assert.Throws<InvalidOperationException>(() => bar.Update("Bar", isActive: false, assignedProducts: 3));

        bar.Update("Bar", isActive: false, assignedProducts: 0);
        Assert.False(bar.IsActive);
    }

    [Fact]
    public void A_variant_parent_is_never_ordered_so_it_takes_no_kitchen_station()
    {
        var parent = Product.Create(
            Organization, ProductType.Goods, "T-shirt", "P1", Guid.NewGuid(), Guid.NewGuid(), null, true, 100m, 50m,
            VatRate.ThirteenPercentVat, 0, trackInventory: false);
        parent.MarkHasVariants();

        Assert.Throws<InvalidOperationException>(() => parent.AssignKitchenStation(Bar));
        parent.AssignKitchenStation(null);
    }
}
