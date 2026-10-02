using ErpApp.Application.Common.Exceptions;
using ErpApp.Application.Common.Security;
using ErpApp.Application.Pos.Commands.CreatePosOrderInvoice;
using ErpApp.Application.Pos.Commands.CreatePosSale;
using ErpApp.Application.Pos.Queries.GetPosKitchenBoard;
using ErpApp.Application.Pos.Queries.GetPosRestaurant;
using ErpApp.Application.Pos.Restaurant;
using ErpApp.Application.UnitTests.TestSupport;
using ErpApp.Domain.Common;
using ErpApp.Domain.Pos;
using ErpApp.Domain.Sales;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.UnitTests.Pos;

/// <summary>
/// Phase 65 -- billing a restaurant order through the till's sale engine, and the kitchen board. Phase
/// 59's table is the running example: 2 Chicken Momo (200, 10% service charge, the Kitchen) and 2 Coke
/// (60, Default), 632.80 rounded to 633, which the vendor split into 317 + 294 and so billed 20 of
/// service charge instead of 40 (its defect 1).
/// </summary>
public class PosRestaurantSettlingTests
{
    private static async Task<(PosTestRestaurant R, PosOrderDto Order, Guid SessionId)> PhaseFiftyNineTableAsync()
    {
        var r = await PosTestRestaurant.CreateAsync();
        var order = await r.SeatAsync(r.T1, [r.Momo(2), r.Coke(2)]);
        var session = await r.Till.OpenAsync();
        return (r, order, session.Id);
    }

    private static PosOrderLineQuantityInput Momo(PosOrderDto order, decimal quantity) =>
        PosTestRestaurant.Line(order, "Chicken Momo", quantity);

    private static PosOrderLineQuantityInput Coke(PosOrderDto order, decimal quantity) =>
        PosTestRestaurant.Line(order, "Coke 250ml", quantity);

    [Fact]
    public async Task A_split_by_item_bills_the_service_charge_on_both_invoices_and_settles_the_table()
    {
        var (r, order, sessionId) = await PhaseFiftyNineTableAsync();

        var preview = await r.PreviewAsync(order.Id, PosOrderSplit.Items, [Momo(order, 1), Coke(order, 1)]);
        Assert.Equal(316m, preview.Total);
        Assert.Equal(20m, preview.ServiceCharge);
        Assert.Equal(633m, preview.OrderTotal);
        Assert.Equal(317m, preview.LeftAfter);

        var first = await r.BillAsync(
            sessionId, order.Id, PosOrderSplit.Items, [r.Till.Cash(500)], [Momo(order, 1), Coke(order, 1)], change: 184m);
        Assert.Equal(preview.Total, first.GrandTotal);
        Assert.Equal(20m, first.ServiceCharge);
        Assert.Equal(PosOrderStatus.Open, first.OrderStatus);
        Assert.Equal(317m, first.OrderToBill);

        var rest = await r.BillAsync(sessionId, order.Id, PosOrderSplit.Whole, [r.Till.Card(317)]);
        Assert.Equal(317m, rest.GrandTotal);
        Assert.Equal(20m, rest.ServiceCharge);
        Assert.Equal(PosOrderStatus.Settled, rest.OrderStatus);
        Assert.Equal(0m, rest.OrderToBill);

        var invoices = await r.Till.Db.Invoices.Include(x => x.Lines).Where(x => x.PosOrderId == order.Id).ToListAsync();
        Assert.Equal(2, invoices.Count);
        Assert.Equal(40m, invoices.Sum(x => x.ServiceChargeTotal));
        Assert.Equal(633m, invoices.Sum(x => x.GrandTotal));
        Assert.All(invoices, x => Assert.Equal(SalesChannel.Pos, x.Channel));
        Assert.All(invoices.SelectMany(x => x.Lines), l => Assert.NotNull(l.PosOrderLineId));

        // Each bill is a till sale: its own entry and its tenders' entry.
        foreach (var invoice in invoices)
        {
            Assert.Equal(2, await r.Till.Db.GlJournalEntries.CountAsync(
                x => x.SourceDocumentType == DocumentType.Invoice && x.SourceDocumentId == invoice.Id));
        }

        var settled = await r.GetAsync(order.Id);
        Assert.Equal(PosOrderStatus.Settled, settled.Status);
        Assert.NotNull(settled.SettledAt);
        Assert.All(settled.Lines, l => Assert.Equal((2m, 0m), (l.Invoiced, l.ToBill)));
        Assert.Equal(633m, settled.Billed);

        // The table is free again: a new order can be seated at it.
        var next = await r.SeatAsync(r.T1, [r.Coke(1)]);
        Assert.Equal(PosOrderStatus.Open, next.Status);
    }

    [Fact]
    public async Task An_equal_split_in_two_is_316_then_317_and_the_preview_is_the_bill()
    {
        var (r, order, sessionId) = await PhaseFiftyNineTableAsync();

        var preview = await r.PreviewAsync(order.Id, PosOrderSplit.Equal, parts: 2);
        Assert.Equal([1m, 1m], preview.Lines.Select(x => x.Quantity));

        var first = await r.BillAsync(sessionId, order.Id, PosOrderSplit.Equal, [r.Till.Cash(316)], parts: 2);
        Assert.Equal(preview.Total, first.GrandTotal);
        Assert.Equal(316m, first.GrandTotal);

        var second = await r.BillAsync(sessionId, order.Id, PosOrderSplit.Equal, [r.Till.Cash(317)], parts: 1);
        Assert.Equal(317m, second.GrandTotal);
        Assert.Equal(0.60m, second.RoundOff);
        Assert.Equal(PosOrderStatus.Settled, second.OrderStatus);
    }

    [Fact]
    public async Task Billing_needs_the_callers_own_open_drawer_and_cannot_overbill()
    {
        var (r, order, mine) = await PhaseFiftyNineTableAsync();

        await Assert.ThrowsAsync<NotFoundException>(() =>
            r.BillAsync(Guid.NewGuid(), order.Id, PosOrderSplit.Whole, [r.Till.Cash(633)]));

        var someoneElse = Guid.NewGuid();
        await PermissionGrantSeed.GrantAsync(r.Till.Db, r.OrganizationId, someoneElse, PosTestRestaurant.WaiterKeys);
        var theirs = await r.Till.OpenAsync(userId: someoneElse);
        await Assert.ThrowsAsync<ForbiddenException>(() =>
            r.BillAsync(theirs.Id, order.Id, PosOrderSplit.Whole, [r.Till.Cash(633)]));

        await Assert.ThrowsAsync<ConflictException>(() =>
            r.BillAsync(mine, order.Id, PosOrderSplit.Items, [r.Till.Cash(500)], [Momo(order, 3)]));

        // The walk-in is never given credit, here as at the Retail till (phase 61 Decision G).
        await Assert.ThrowsAsync<ConflictException>(() =>
            r.BillAsync(mine, order.Id, PosOrderSplit.Whole, [r.Till.Cash(600)]));
    }

    [Fact]
    public async Task Voiding_a_bill_gives_its_quantities_back_and_reopens_a_settled_order()
    {
        var (r, order, sessionId) = await PhaseFiftyNineTableAsync();
        var bill = await r.BillAsync(sessionId, order.Id, PosOrderSplit.Whole, [r.Till.Cash(633)]);
        Assert.Equal(PosOrderStatus.Settled, bill.OrderStatus);

        await r.Till.VoidAsync(bill.Id);

        var reopened = await r.GetAsync(order.Id);
        Assert.Equal(PosOrderStatus.Open, reopened.Status);
        Assert.Null(reopened.SettledAt);
        Assert.All(reopened.Lines, l => Assert.Equal((0m, 2m), (l.Invoiced, l.ToBill)));
        Assert.True(Assert.Single(reopened.Invoices).IsVoided);

        var again = await r.BillAsync(sessionId, order.Id, PosOrderSplit.Whole, [r.Till.Cash(633)]);
        Assert.Equal(633m, again.GrandTotal);
    }

    [Fact]
    public async Task A_bill_cannot_be_voided_once_its_table_seats_another_order()
    {
        var (r, order, sessionId) = await PhaseFiftyNineTableAsync();
        var bill = await r.BillAsync(sessionId, order.Id, PosOrderSplit.Whole, [r.Till.Cash(633)]);
        var next = await r.SeatAsync(r.T1, [r.Coke(1)]);

        var refused = await Assert.ThrowsAsync<ConflictException>(() => r.Till.VoidAsync(bill.Id));
        Assert.Contains(next.Code, refused.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task What_is_billed_cannot_be_discarded_and_discarding_the_rest_settles_the_order()
    {
        var (r, order, sessionId) = await PhaseFiftyNineTableAsync();
        await r.BillAsync(sessionId, order.Id, PosOrderSplit.Items, [r.Till.Cash(249)], [Momo(order, 1)]);

        await Assert.ThrowsAsync<ConflictException>(() => r.DiscardAsync(order.Id, "typo", Momo(order, 2)));
        await Assert.ThrowsAsync<ConflictException>(() => r.VoidAsync(order.Id));

        var after = await r.DiscardAsync(order.Id, "Guests left", Momo(order, 1), Coke(order, 2));
        Assert.Equal(PosOrderStatus.Settled, after.Status);
        Assert.Equal(0m, after.ToBill);
    }

    [Fact]
    public async Task The_floor_says_who_holds_a_drawer_and_what_a_tab_has_billed()
    {
        var (r, order, sessionId) = await PhaseFiftyNineTableAsync();
        await r.BillAsync(sessionId, order.Id, PosOrderSplit.Items, [r.Till.Cash(249)], [Momo(order, 1)]);

        var floor = await new GetPosRestaurantQueryHandler(r.Till.Db, r.CurrentUser()).Handle(
            new GetPosRestaurantQuery(r.OrganizationId, r.LocationId), CancellationToken.None);

        Assert.Equal(sessionId, floor.MySessionId);
        Assert.True(floor.CanKitchen);
        Assert.True(floor.PrintEstimateBill);
        var tab = Assert.Single(floor.Orders);
        Assert.Equal(249m, tab.Billed);
        Assert.Equal(3m, tab.ToBill);
    }

    [Fact]
    public async Task The_board_shows_each_station_its_pending_tickets_and_the_kitchen_serves_from_it()
    {
        var r = await PosTestRestaurant.CreateAsync();
        var order = await r.SeatAsync(r.T1, [r.Momo(2, "less spicy"), r.Coke(1)]);

        var board = await r.BoardAsync();
        Assert.Equal(2, board.PendingCount);
        Assert.Equal(["Kitchen", KitchenStation.DefaultName], board.Tickets.Select(x => x.KitchenStationName).Order(StringComparer.Ordinal).Reverse());
        var kitchen = board.Tickets.Single(x => x.KitchenStationId == r.KitchenId);
        Assert.Equal("T1", kitchen.Label);
        Assert.Equal(("Chicken Momo", "less spicy", 2m), (kitchen.Lines[0].ProductName, kitchen.Lines[0].Note, kitchen.Lines[0].Pending));
        Assert.Contains(board.Summary, x => x.ProductName == "Chicken Momo" && x.Pending == 2m);

        var onlyKitchen = await r.BoardAsync(stationId: r.KitchenId);
        Assert.Equal(kitchen.Id, Assert.Single(onlyKitchen.Tickets).Id);
        var onlyDefault = await r.BoardAsync(defaultStation: true);
        Assert.Equal(KitchenStation.DefaultName, Assert.Single(onlyDefault.Tickets).KitchenStationName);

        // A part, then the rest; never more than the ticket still has to cook.
        var line = kitchen.Lines[0].OrderLineId;
        await Assert.ThrowsAsync<ConflictException>(() => r.ServeTicketAsync(order.Id, kitchen.Id, new PosOrderLineQuantityInput(line, 3m)));
        var part = await r.ServeTicketAsync(order.Id, kitchen.Id, new PosOrderLineQuantityInput(line, 1m));
        Assert.Equal((KitchenTicketState.Pending, 1m), (part.State, part.Pending));
        var all = await r.ServeTicketAsync(order.Id, kitchen.Id);
        Assert.Equal(KitchenTicketState.Served, all.State);

        board = await r.BoardAsync();
        Assert.Equal(1, board.PendingCount);
        var served = await r.BoardAsync(PosKitchenBoardView.Served);
        Assert.Equal(kitchen.Id, Assert.Single(served.Tickets).Id);

        // The waiter's screen sees the cook's serve: one counter.
        Assert.Equal(2m, (await r.GetAsync(order.Id)).Lines.Single(x => x.ProductName == "Chicken Momo").Served);
    }

    [Fact]
    public async Task A_cancellation_reaches_the_board_and_has_nothing_to_serve()
    {
        var r = await PosTestRestaurant.CreateAsync();
        var order = await r.SeatAsync(r.T1, [r.Coke(2)]);
        order = await r.DiscardAsync(order.Id, "Guest changed their mind", PosTestRestaurant.Line(order, "Coke 250ml", 1m));

        var all = await r.BoardAsync(PosKitchenBoardView.All);
        var cancellation = all.Tickets.Single(x => x.State == KitchenTicketState.Cancellation);
        Assert.Equal("Guest changed their mind", cancellation.Reason);
        var send = all.Tickets.Single(x => x.State == KitchenTicketState.Pending);
        Assert.Equal((2m, 1m, 1m), (send.Lines[0].Sent, send.Lines[0].Cancelled, send.Lines[0].Pending));

        await Assert.ThrowsAsync<ConflictException>(() => r.ServeTicketAsync(order.Id, cancellation.Id));
    }

    [Fact]
    public async Task A_paid_take_away_stays_on_the_board_until_it_is_served()
    {
        var r = await PosTestRestaurant.CreateAsync();
        var order = await r.OpenAsync(PosTab.TakeAway, [r.Momo(1)]);
        var session = await r.Till.OpenAsync();

        var bill = await r.BillAsync(session.Id, order.Id, PosOrderSplit.Whole, [r.Till.Cash(226)]);
        Assert.Equal(0m, bill.ServiceCharge);
        Assert.Equal(PosOrderStatus.Settled, bill.OrderStatus);

        var board = await r.BoardAsync(orderType: PosTab.TakeAway);
        var ticket = Assert.Single(board.Tickets);
        Assert.Equal(PosOrderStatus.Settled, ticket.OrderStatus);

        await r.ServeTicketAsync(order.Id, ticket.Id);
        Assert.Equal(0, (await r.BoardAsync()).PendingCount);
    }
}
