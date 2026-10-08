using ErpApp.Domain.Catalog;
using ErpApp.Domain.Pos;
using ErpApp.Domain.Tenancy;

namespace ErpApp.Domain.UnitTests.Pos;

/// <summary>
/// Phase 68 -- Mark as Take Away and item transfer: both are quantity moves recorded as kitchen tickets, so a
/// line still stores no quantity. Phase 59's table is the running example: 2 Chicken Momo (200, 10% service
/// charge, the Kitchen) and 2 Coke 250ml (60, no service charge), 13% VAT, 632.80 in all. One Momo is 248.60
/// with its service charge (200 + 20 + 13% of 220) and 226.00 without.
/// </summary>
public class PosTakeAwayAndTransferTests
{
    private static readonly Guid Organization = Guid.NewGuid();
    private static readonly Guid Location = Guid.NewGuid();
    private static readonly Guid User = Guid.NewGuid();
    private static readonly Guid Kitchen = Guid.NewGuid();
    private static readonly Guid MomoProduct = Guid.NewGuid();
    private static readonly Guid CokeProduct = Guid.NewGuid();
    private static readonly IReadOnlyDictionary<Guid, decimal> NothingBilled = new Dictionary<Guid, decimal>();

    private static PosOrderLineQuantity Q(PosOrderLine line, decimal quantity) => new(line.Id, quantity);

    private static PosOrder Table(string code = "ORD0001", PosTab type = PosTab.DineIn, Guid? location = null)
    {
        var order = PosOrder.Open(
            Organization, location ?? Location, code, type, type == PosTab.DineIn ? Guid.NewGuid() : null,
            type == PosTab.DineIn ? 2 : 0, null, User, DateTimeOffset.UtcNow);
        order.Send(
            [
                new PosOrderNewLine(MomoProduct, null, 1m, 2m, 200m, VatRate.ThirteenPercentVat, 10m, null, Kitchen),
                new PosOrderNewLine(CokeProduct, null, 1m, 2m, 60m, VatRate.ThirteenPercentVat, 0m, null, null),
            ],
            [], User);
        return order;
    }

    private static decimal Total(PosOrder order)
    {
        var e = order.Estimate();
        return e.Amount + e.ServiceChargeAmount + e.VatAmount;
    }

    private static PosOrderLine TakeAwayOf(PosOrder order, PosOrderLine line) =>
        order.Lines.Single(x => x.ParcelledFromLineId == line.Id);

    // ---- Mark as Take Away ---------------------------------------------------------------------

    [Fact]
    public void Marking_moves_the_quantity_to_a_take_away_line_through_one_ticket_and_keeps_the_service_charge_when_the_setting_is_on()
    {
        var order = Table();
        var momo = order.Lines[0];

        var result = order.MarkTakeAway(momo.Id, 1m, serviceChargeOnTakeAway: true, User, NothingBilled);

        var parcel = Assert.Single(result.NewLines);
        Assert.True(parcel.IsTakeAway);
        Assert.Equal(momo.Id, parcel.ParcelledFromLineId);
        Assert.Equal(10m, parcel.ServiceChargeRate);
        Assert.Equal(momo.Rate, parcel.Rate);
        Assert.Equal(Kitchen, parcel.KitchenStationId);

        // The dine-in line was ordered 2 and moved 1 out; nothing was discarded.
        var q = order.QuantitiesOf(momo);
        Assert.Equal((2m, 0m, 1m, 1m), (q.Ordered, q.Discarded, q.Net, q.MovedOut));
        Assert.Equal(1m, order.QuantitiesOf(parcel).Net);
        Assert.Equal(1m, order.QuantitiesOf(parcel).MovedIn);

        var ticket = Assert.Single(result.NewTickets);
        Assert.Equal(KitchenTicketKind.TakeAway, ticket.Kind);
        Assert.Equal(Kitchen, ticket.KitchenStationId);
        Assert.Equal([-1m, 1m], ticket.Lines.Select(x => x.Quantity).Order());

        // On is today's behaviour: the table pays exactly what it did.
        Assert.Equal(632.80m, Total(order));
    }

    [Fact]
    public void With_the_setting_off_the_parcelled_quantity_carries_no_service_charge()
    {
        var order = Table();

        order.MarkTakeAway(order.Lines[0].Id, 1m, serviceChargeOnTakeAway: false, User, NothingBilled);

        Assert.Equal(0m, TakeAwayOf(order, order.Lines[0]).ServiceChargeRate);
        Assert.Equal(632.80m - 248.60m + 226.00m, Total(order));
    }

    [Fact]
    public void A_product_exempt_from_service_charge_stays_exempt_when_parcelled_whatever_the_setting()
    {
        var order = Table();
        var coke = order.Lines[1];

        order.MarkTakeAway(coke.Id, 1m, serviceChargeOnTakeAway: true, User, NothingBilled);

        Assert.Equal(0m, TakeAwayOf(order, coke).ServiceChargeRate);
        Assert.Equal(632.80m, Total(order));
    }

    [Fact]
    public void A_second_mark_joins_the_take_away_line_unless_the_setting_changed_in_between()
    {
        var order = Table();
        var momo = order.Lines[0];

        order.MarkTakeAway(momo.Id, 0.5m, true, User, NothingBilled);
        var again = order.MarkTakeAway(momo.Id, 0.5m, true, User, NothingBilled);
        Assert.Empty(again.NewLines);
        Assert.Equal(1m, order.QuantitiesOf(TakeAwayOf(order, momo)).Net);

        // Frozen when marked: the setting switched off now reprices nothing already parcelled.
        var other = Table("ORD0002");
        other.MarkTakeAway(other.Lines[0].Id, 1m, true, User, NothingBilled);
        var later = other.MarkTakeAway(other.Lines[0].Id, 1m, false, User, NothingBilled);
        Assert.Equal(0m, Assert.Single(later.NewLines).ServiceChargeRate);
        Assert.Equal([0m, 10m], other.Lines.Where(x => x.IsTakeAway).Select(x => x.ServiceChargeRate).Order());
    }

    [Fact]
    public void Only_food_not_yet_served_and_not_yet_billed_can_be_marked()
    {
        var order = Table();
        var momo = order.Lines[0];
        order.Serve([Q(momo, 1m)]);

        var served = Assert.Throws<InvalidOperationException>(() => order.MarkTakeAway(momo.Id, 2m, true, User, NothingBilled));
        Assert.Contains("1 not yet served or billed", served.Message, StringComparison.Ordinal);

        var billed = new Dictionary<Guid, decimal> { [momo.Id] = 2m };
        Assert.Throws<InvalidOperationException>(() => order.MarkTakeAway(momo.Id, 1m, true, User, billed));

        order.MarkTakeAway(momo.Id, 1m, true, User, NothingBilled);
    }

    [Fact]
    public void Only_a_dine_in_line_is_marked_and_a_take_away_line_cannot_be_marked_again()
    {
        var parcel = Table(type: PosTab.TakeAway);
        Assert.Throws<InvalidOperationException>(() => parcel.MarkTakeAway(parcel.Lines[0].Id, 1m, true, User, NothingBilled));

        var order = Table();
        order.MarkTakeAway(order.Lines[0].Id, 1m, true, User, NothingBilled);
        var line = TakeAwayOf(order, order.Lines[0]);
        Assert.Throws<InvalidOperationException>(() => order.MarkTakeAway(line.Id, 1m, true, User, NothingBilled));
    }

    [Fact]
    public void The_kitchen_sees_the_send_with_one_moved_and_the_take_away_ticket_pending_until_it_is_packed()
    {
        var order = Table();
        var momo = order.Lines[0];
        var mark = order.MarkTakeAway(momo.Id, 1m, true, User, NothingBilled);
        var parcel = Assert.Single(mark.NewLines);

        var send = order.TicketProgress().Single(x => x.TicketId == order.Tickets.First(t => t.KitchenStationId == Kitchen).Id);
        var sent = send.Lines.Single(x => x.OrderLineId == momo.Id);
        Assert.Equal((2m, 0m, 1m, 1m), (sent.Sent, sent.Cancelled, sent.Moved, sent.Pending));

        var takeAway = order.TicketProgress().Single(x => x.TicketId == mark.NewTickets[0].Id);
        Assert.Equal(KitchenTicketState.Pending, takeAway.State);

        order.Serve([Q(parcel, 1m)]);
        Assert.Equal(KitchenTicketState.Served, order.TicketProgress().Single(x => x.TicketId == mark.NewTickets[0].Id).State);
    }

    [Fact]
    public void A_bill_over_a_parcelled_quantity_prices_each_line_at_its_own_frozen_rate_and_the_parts_add_up()
    {
        var order = Table();
        var momo = order.Lines[0];
        order.MarkTakeAway(momo.Id, 1m, serviceChargeOnTakeAway: false, User, NothingBilled);
        var parcel = TakeAwayOf(order, momo);

        // The parcel alone, then the rest: 226 + 384.20 is the order's 610.20, rounded on the running total.
        var first = PosOrderBill.Plan(
            order, new Dictionary<Guid, PosOrderLineBilled>(), PosOrderBilledTotals.None, PosOrderSplit.Items,
            [Q(parcel, 1m)], 0, roundOff: true);
        Assert.Equal(226.00m, first.Unrounded);
        Assert.Equal(0m, first.ServiceCharge);

        var billed = first.Lines.ToDictionary(x => x.Line.Id, x => new PosOrderLineBilled(x.Quantity, x.Figures));
        var rest = PosOrderBill.Plan(
            order, billed, new PosOrderBilledTotals(first.Unrounded, first.Total), PosOrderSplit.Whole, [], 0, roundOff: true);

        Assert.Equal(20m, rest.ServiceCharge);
        Assert.Equal(610m, first.Total + rest.Total);
    }

    // ---- Transfer --------------------------------------------------------------------------------

    [Fact]
    public void A_transfer_moves_the_quantity_to_the_other_table_at_the_same_rates_through_a_ticket_on_each_order()
    {
        var source = Table("ORD0001");
        var target = Table("ORD0002");
        var momo = source.Lines[0];

        var result = PosOrder.Transfer(source, target, [Q(momo, 1m)], User, NothingBilled, DateTimeOffset.UtcNow);

        // The target already had Momo on the same terms, so the quantity joins that line.
        Assert.Empty(result.Target.NewLines);
        Assert.Equal(1m, source.QuantitiesOf(momo).Net);
        Assert.Equal(3m, target.QuantitiesOf(target.Lines[0]).Net);
        Assert.Equal(632.80m - 248.60m, Total(source));
        Assert.Equal(632.80m + 248.60m, Total(target));

        var outgoing = Assert.Single(result.Source.NewTickets);
        var incoming = Assert.Single(result.Target.NewTickets);
        Assert.Equal((KitchenTicketKind.Transfer, target.Id, -1m), (outgoing.Kind, outgoing.CounterpartOrderId, outgoing.Lines.Single().Quantity));
        Assert.Equal((KitchenTicketKind.Transfer, source.Id, 1m), (incoming.Kind, incoming.CounterpartOrderId, incoming.Lines.Single().Quantity));
        Assert.False(result.SourceEmptied);
    }

    [Fact]
    public void A_transfer_to_a_table_without_the_dish_copies_the_lines_frozen_terms_including_a_take_away()
    {
        var source = Table("ORD0001");
        source.MarkTakeAway(source.Lines[0].Id, 1m, serviceChargeOnTakeAway: false, User, NothingBilled);
        var parcel = TakeAwayOf(source, source.Lines[0]);

        var target = PosOrder.Open(Organization, Location, "ORD0002", PosTab.DineIn, Guid.NewGuid(), 1, null, User, DateTimeOffset.UtcNow);
        var result = PosOrder.Transfer(source, target, [Q(parcel, 1m)], User, NothingBilled, DateTimeOffset.UtcNow);

        var copy = Assert.Single(result.Target.NewLines);
        Assert.Equal((true, 0m, 200m, Kitchen), (copy.IsTakeAway, copy.ServiceChargeRate, copy.Rate, copy.KitchenStationId));
        Assert.Null(copy.ParcelledFromLineId);
        Assert.Equal(226.00m, Total(target));
    }

    [Fact]
    public void Served_food_moves_after_the_unserved_and_takes_its_served_count_with_it()
    {
        var source = Table("ORD0001");
        var target = PosOrder.Open(Organization, Location, "ORD0002", PosTab.DineIn, Guid.NewGuid(), 1, null, User, DateTimeOffset.UtcNow);
        var momo = source.Lines[0];
        source.Serve([Q(momo, 1m)]);

        var result = PosOrder.Transfer(source, target, [Q(momo, 2m)], User, NothingBilled, DateTimeOffset.UtcNow);

        Assert.Equal(0m, momo.ServedQuantity);
        var moved = Assert.Single(result.Target.NewLines);
        Assert.Equal(1m, moved.ServedQuantity);
        Assert.Equal(1m, target.QuantitiesOf(moved).Outstanding);
    }

    [Fact]
    public void Only_what_is_unbilled_can_be_transferred_so_a_bill_and_its_void_stay_with_their_own_line()
    {
        var source = Table("ORD0001");
        var target = Table("ORD0002");
        var momo = source.Lines[0];
        var billed = new Dictionary<Guid, decimal> { [momo.Id] = 1m };

        var refused = Assert.Throws<InvalidOperationException>(() =>
            PosOrder.Transfer(source, target, [Q(momo, 2m)], User, billed, DateTimeOffset.UtcNow));
        Assert.Contains("1 not yet billed", refused.Message, StringComparison.Ordinal);

        PosOrder.Transfer(source, target, [Q(momo, 1m)], User, billed, DateTimeOffset.UtcNow);

        // The source line still holds exactly its billed quantity; a void of that bill would give it back
        // to this line, which has room for it.
        Assert.Equal(1m, source.QuantitiesOf(momo).Net);
        Assert.Equal(0m, source.RemainingToBill(momo, billed));
        Assert.Equal(1m, source.RemainingToBill(momo, NothingBilled));
    }

    [Fact]
    public void A_transfer_that_empties_an_unbilled_order_voids_it_and_one_that_leaves_it_billed_lets_it_settle()
    {
        var source = Table("ORD0001");
        var target = Table("ORD0002");

        var all = PosOrder.Transfer(
            source, target, [Q(source.Lines[0], 2m), Q(source.Lines[1], 2m)], User, NothingBilled, DateTimeOffset.UtcNow);

        Assert.True(all.SourceEmptied);
        Assert.Equal(PosOrderStatus.Voided, source.Status);
        Assert.Equal("All items transferred to ORD0002", source.VoidReason);

        var billedSource = Table("ORD0003");
        var billed = new Dictionary<Guid, decimal> { [billedSource.Lines[1].Id] = 2m };
        var rest = PosOrder.Transfer(billedSource, Table("ORD0004"), [Q(billedSource.Lines[0], 2m)], User, billed, DateTimeOffset.UtcNow);

        Assert.False(rest.SourceEmptied);
        Assert.True(billedSource.SettleIfFullyBilled(billed, DateTimeOffset.UtcNow));
    }

    [Fact]
    public void A_transfer_is_between_two_open_dine_in_orders_at_one_location()
    {
        var source = Table("ORD0001");

        Assert.Throws<InvalidOperationException>(() =>
            PosOrder.Transfer(source, source, [Q(source.Lines[0], 1m)], User, NothingBilled, DateTimeOffset.UtcNow));
        Assert.Throws<InvalidOperationException>(() =>
            PosOrder.Transfer(source, Table("ORD0002", PosTab.TakeAway), [Q(source.Lines[0], 1m)], User, NothingBilled, DateTimeOffset.UtcNow));
        Assert.Throws<InvalidOperationException>(() =>
            PosOrder.Transfer(source, Table("ORD0003", location: Guid.NewGuid()), [Q(source.Lines[0], 1m)], User, NothingBilled, DateTimeOffset.UtcNow));

        var voided = Table("ORD0004");
        voided.Void("Guests left", User, DateTimeOffset.UtcNow, NothingBilled);
        Assert.Throws<InvalidOperationException>(() =>
            PosOrder.Transfer(source, voided, [Q(source.Lines[0], 1m)], User, NothingBilled, DateTimeOffset.UtcNow));
    }

    [Fact]
    public void The_kitchen_sees_the_outgoing_transfer_as_moved_and_the_incoming_one_as_food_to_bring()
    {
        var source = Table("ORD0001");
        var target = PosOrder.Open(Organization, Location, "ORD0002", PosTab.DineIn, Guid.NewGuid(), 1, null, User, DateTimeOffset.UtcNow);

        var result = PosOrder.Transfer(source, target, [Q(source.Lines[0], 2m)], User, NothingBilled, DateTimeOffset.UtcNow);

        Assert.Equal(KitchenTicketState.Moved, source.TicketProgress().Single(x => x.TicketId == result.Source.NewTickets[0].Id).State);
        // The original send's Momo all moved before any was served: the send reads as moved, not cancelled.
        var send = source.Tickets.First(t => t.Kind == KitchenTicketKind.Send && t.KitchenStationId == Kitchen);
        Assert.Equal(KitchenTicketState.Moved, source.TicketProgress().Single(x => x.TicketId == send.Id).State);
        Assert.Equal(KitchenTicketState.Pending, target.TicketProgress().Single(x => x.TicketId == result.Target.NewTickets[0].Id).State);
    }
}
