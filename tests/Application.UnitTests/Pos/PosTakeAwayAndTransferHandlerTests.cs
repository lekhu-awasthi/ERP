using ErpApp.Application.Common.Exceptions;
using ErpApp.Application.Pos.Commands.CreatePosOrderTransfer;
using ErpApp.Application.Pos.Commands.UpdatePosOrderTakeAway;
using ErpApp.Application.Pos.Queries.GetPosKitchenBoard;
using ErpApp.Application.Pos.Restaurant;
using ErpApp.Domain.Pos;
using ErpApp.Domain.Tenancy;
using FluentValidation;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.UnitTests.Pos;

/// <summary>
/// Phase 68 -- Mark as Take Away and item transfer through their handlers, billed through the till. Phase
/// 59's table is the running example: 2 Chicken Momo (200, 10% service charge, the Kitchen) and 2 Coke (60,
/// Default), 632.80 in all; one Momo is 248.60 with its service charge and 226.00 without.
/// </summary>
public class PosTakeAwayAndTransferHandlerTests
{
    private static async Task<(PosTestRestaurant R, PosOrderDto Order)> TableAsync()
    {
        var r = await PosTestRestaurant.CreateAsync();
        var order = await r.SeatAsync(r.T1, [r.Momo(2), r.Coke(2)]);
        return (r, order);
    }

    private static Guid MomoLine(PosOrderDto order) =>
        order.Lines.Single(x => x.ProductName == "Chicken Momo" && !x.IsTakeAway).Id;

    private static Task<PosOrderDto> MarkAsync(PosTestRestaurant r, Guid orderId, Guid lineId, decimal quantity) =>
        new UpdatePosOrderTakeAwayCommandHandler(r.Till.Db, r.CurrentUser()).Handle(
            new UpdatePosOrderTakeAwayCommand(r.OrganizationId, orderId, lineId, quantity), CancellationToken.None);

    private static Task<PosOrderTransferDto> TransferAsync(
        PosTestRestaurant r, Guid orderId, Guid tableId, params PosOrderLineQuantityInput[] items) =>
        new CreatePosOrderTransferCommandHandler(r.Till.Db, r.Till.Seed.NumberGenerator, r.CurrentUser()).Handle(
            new CreatePosOrderTransferCommand(r.OrganizationId, orderId, tableId, items), CancellationToken.None);

    private static async Task SetTakeAwayServiceChargeAsync(PosTestRestaurant r, bool on)
    {
        var settings = await r.Till.Db.PosLocationSettings.SingleAsync();
        settings.Update(
            PosMode.Restaurant, serviceChargeEnabled: true, serviceChargeRate: 10m, serviceChargeAccountId: null,
            serviceChargeOnTakeAway: on, roundOffEnabled: true, roundOffAccountId: null, cashVerificationRequired: false,
            denominations: PosLocationSettings.DefaultDenominations, defaultTab: null, printEstimateBill: true,
            printInvoice: true, printCreditNote: true, printKot: false, abbreviatedTaxInvoiceEnabled: false);
        await r.Till.Db.SaveChangesAsync();
    }

    [Fact]
    public async Task A_location_reads_service_charge_on_take_away_as_on_until_it_is_switched_off()
    {
        var r = await PosTestRestaurant.CreateAsync();

        Assert.True(PosLocationSettings.CreateDefault(r.OrganizationId, r.LocationId).ServiceChargeOnTakeAway);
        Assert.True((await r.Till.Db.PosLocationSettings.SingleAsync()).ServiceChargeOnTakeAway);
    }

    [Fact]
    public async Task Marking_under_each_setting_freezes_its_rate_and_the_bill_charges_exactly_that()
    {
        var (r, order) = await TableAsync();

        // On (the default): the parcel keeps its service charge, so the table pays what it did.
        var marked = await MarkAsync(r, order.Id, MomoLine(order), 1m);
        var keeps = Assert.Single(marked.Lines, x => x.IsTakeAway);
        Assert.Equal((10m, 1m, MomoLine(order)), (keeps.ServiceChargeRate, keeps.Quantity, keeps.ParcelledFromLineId!.Value));
        Assert.Equal(632.80m, marked.Total);
        Assert.Equal(1m, marked.Lines.Single(x => x.Id == MomoLine(order)).MovedOut);

        // Off: the next parcel carries none; the first keeps the rate it was marked at.
        await SetTakeAwayServiceChargeAsync(r, on: false);
        var again = await MarkAsync(r, order.Id, MomoLine(order), 1m);
        Assert.Equal([0m, 10m], again.Lines.Where(x => x.IsTakeAway).Select(x => x.ServiceChargeRate).Order());
        Assert.Equal(632.80m - 22.60m, again.Total);

        var session = await r.Till.OpenAsync();
        var bill = await r.BillAsync(session.Id, order.Id, PosOrderSplit.Whole, [r.Till.Cash(610)]);
        Assert.Equal(610m, bill.GrandTotal);
        Assert.Equal(20m, bill.ServiceCharge);

        var invoice = await r.Till.Db.Invoices.Include(x => x.Lines).SingleAsync(x => x.PosOrderId == order.Id);
        var parcelLines = again.Lines.Where(x => x.IsTakeAway).Select(x => x.Id).ToHashSet();
        Assert.Equal(2, invoice.Lines.Count(l => l.PosOrderLineId is { } id && parcelLines.Contains(id)));
        Assert.DoesNotContain(invoice.Lines, l => l.PosOrderLineId == MomoLine(order));
    }

    [Fact]
    public async Task A_take_away_mark_reaches_the_board_as_a_ticket_to_pack_and_is_served_from_it()
    {
        var (r, order) = await TableAsync();
        var marked = await MarkAsync(r, order.Id, MomoLine(order), 1m);

        var board = await r.BoardAsync(stationId: r.KitchenId);
        var card = Assert.Single(board.Tickets, x => x.Kind == KitchenTicketKind.TakeAway);
        var pack = Assert.Single(card.Lines, x => x.Sent > 0m);
        Assert.True(pack.IsTakeAway);
        Assert.Equal(1m, pack.Pending);

        // The original send now owes one Momo, not two.
        var send = Assert.Single(board.Tickets, x => x.Kind == KitchenTicketKind.Send);
        Assert.Equal((1m, 1m), (send.Lines.Single().Moved, send.Lines.Single().Pending));

        await r.ServeTicketAsync(order.Id, card.Id);
        var after = await r.BoardAsync(stationId: r.KitchenId);
        Assert.DoesNotContain(after.Tickets, x => x.Id == card.Id);
        Assert.Equal(1m, (await r.GetAsync(order.Id)).Lines.Single(x => x.IsTakeAway).Served);
        _ = marked;
    }

    [Fact]
    public async Task A_transfer_to_a_free_table_opens_an_order_there_and_both_tabs_bill_to_the_whole()
    {
        var (r, order) = await TableAsync();

        var moved = await TransferAsync(r, order.Id, r.T2, new PosOrderLineQuantityInput(MomoLine(order), 1m));

        Assert.True(moved.TargetCreated);
        Assert.Equal(PosTab.DineIn, moved.Target.OrderType);
        Assert.Equal(r.T2, moved.Target.TableId);
        Assert.Equal(248.60m, moved.Target.Total);
        Assert.Equal(632.80m - 248.60m, moved.Source.Total);
        Assert.Equal(order.Code, Assert.Single(moved.Target.Tickets).CounterpartOrderCode);
        Assert.Equal(moved.Target.Code, moved.Source.Tickets.Single(x => x.Kind == KitchenTicketKind.Transfer).CounterpartOrderCode);

        var session = await r.Till.OpenAsync();
        var t1 = await r.BillAsync(session.Id, order.Id, PosOrderSplit.Whole, [r.Till.Cash(384)]);
        var t2 = await r.BillAsync(session.Id, moved.Target.Id, PosOrderSplit.Whole, [r.Till.Cash(249)]);

        Assert.Equal((384m, 249m), (t1.GrandTotal, t2.GrandTotal));
        Assert.Equal((20m, 20m), (t1.ServiceCharge, t2.ServiceCharge));
        Assert.Equal(PosOrderStatus.Settled, (await r.GetAsync(moved.Target.Id)).Status);
    }

    [Fact]
    public async Task Transferring_everything_into_an_occupied_table_merges_the_lines_and_voids_the_emptied_order()
    {
        var (r, order) = await TableAsync();
        var other = await r.SeatAsync(r.T2, [r.Momo(1)]);

        var moved = await TransferAsync(
            r, order.Id, r.T2,
            PosTestRestaurant.Line(order, "Chicken Momo", 2), PosTestRestaurant.Line(order, "Coke 250ml", 2));

        Assert.False(moved.TargetCreated);
        Assert.Equal(other.Id, moved.Target.Id);
        Assert.Equal(3m, moved.Target.Lines.Single(x => x.ProductName == "Chicken Momo").Quantity);
        Assert.Equal(2, moved.Target.Lines.Count);
        Assert.Equal(PosOrderStatus.Voided, moved.Source.Status);
        Assert.Equal($"All items transferred to {other.Code}", moved.Source.VoidReason);

        // T1 is free again.
        var floor = await r.FloorAsync();
        Assert.False(floor.Areas.SelectMany(a => a.Tables).Single(t => t.Id == r.T1).IsOccupied);
    }

    [Fact]
    public async Task A_transfer_names_another_table_and_moves_only_dine_in_food()
    {
        var (r, order) = await TableAsync();

        var same = await Assert.ThrowsAsync<ValidationException>(() =>
            TransferAsync(r, order.Id, r.T1, new PosOrderLineQuantityInput(MomoLine(order), 1m)));
        Assert.Contains(same.Errors, e => e.PropertyName == "TableId");

        var parcel = await r.OpenAsync(PosTab.TakeAway, [r.Momo(1)]);
        await Assert.ThrowsAsync<ConflictException>(() =>
            TransferAsync(r, parcel.Id, r.T2, new PosOrderLineQuantityInput(parcel.Lines[0].Id, 1m)));

        await Assert.ThrowsAsync<ConflictException>(() =>
            TransferAsync(r, order.Id, r.T2, new PosOrderLineQuantityInput(MomoLine(order), 3m)));

        // A refused transfer opened nothing at the target table.
        Assert.False(await r.Till.Db.PosOrders.AnyAsync(x => x.PosTableId == r.T2));
    }

    [Fact]
    public async Task Marking_refuses_served_food_and_a_take_away_order()
    {
        var (r, order) = await TableAsync();
        await r.ServeAsync(order.Id, new PosOrderLineQuantityInput(MomoLine(order), 2m));

        await Assert.ThrowsAsync<ConflictException>(() => MarkAsync(r, order.Id, MomoLine(order), 1m));

        var parcel = await r.OpenAsync(PosTab.TakeAway, [r.Momo(1)]);
        await Assert.ThrowsAsync<ConflictException>(() => MarkAsync(r, parcel.Id, parcel.Lines[0].Id, 1m));
    }
}
