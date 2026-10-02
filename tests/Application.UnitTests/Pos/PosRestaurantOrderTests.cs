using ErpApp.Application.Common.Exceptions;
using ErpApp.Application.Common.Security;
using ErpApp.Application.Pos.Commands.CreateKitchenStation;
using ErpApp.Application.Pos.Commands.CreatePosOrder;
using ErpApp.Application.Pos.Commands.PrintPosKitchenTicket;
using ErpApp.Application.Pos.Commands.SavePosAreaLayout;
using ErpApp.Application.Pos.Commands.SetKitchenStationProducts;
using ErpApp.Application.Pos.Commands.UpdateKitchenStation;
using ErpApp.Application.Pos.Commands.UpdatePosArea;
using ErpApp.Application.Pos.Commands.UpdatePosOrder;
using ErpApp.Application.Pos.Commands.VoidPosOrder;
using ErpApp.Application.Pos.Commands.VoidPosOrderItems;
using ErpApp.Application.Pos.Commands.CreatePosSale;
using ErpApp.Application.Pos.Queries.GetPosOrder;
using ErpApp.Application.Pos.Queries.GetPosRestaurant;
using ErpApp.Application.Pos.Queries.ListPosOrders;
using ErpApp.Application.Pos.Restaurant;
using ErpApp.Application.UnitTests.TestSupport;
using ErpApp.Domain.Common;
using ErpApp.Domain.Contacts;
using ErpApp.Domain.Pos;
using FluentValidation;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.UnitTests.Pos;

/// <summary>
/// Phase 64 -- the restaurant's handlers: seating a table, the kitchen tickets each send makes, serving,
/// discarding, voiding, the floor, kitchen stations and the ERP's POS Orders list. Phase 59's service is
/// the running example (2 Momo + 1 Coke at a table, then 1 more Coke).
/// </summary>
public class PosRestaurantOrderTests
{
    [Fact]
    public async Task Seating_a_table_prices_from_the_catalogue_and_tickets_each_station()
    {
        var r = await PosTestRestaurant.CreateAsync();

        var order = await r.SeatAsync(r.T1, [r.Momo(2, "less spicy"), r.Coke(1)]);

        Assert.StartsWith(PosOrder.CodePrefix, order.Code, StringComparison.Ordinal);
        Assert.Equal(PosOrderStatus.Open, order.Status);
        Assert.Equal("T1", order.TableName);
        Assert.Equal("Ground Floor", order.AreaName);

        var momo = order.Lines.Single(x => x.ProductName == "Chicken Momo");
        Assert.Equal(200m, momo.Rate);
        Assert.Equal(10m, momo.ServiceChargeRate);
        Assert.Equal("less spicy", momo.Note);
        Assert.Equal("Kitchen", momo.KitchenStationName);
        Assert.Equal(0m, order.Lines.Single(x => x.ProductName == "Coke 250ml").ServiceChargeRate);

        // One ticket per station for the send, Default last, numbered by the order's code and the send.
        Assert.Equal(["Kitchen", KitchenStation.DefaultName], order.Tickets.Select(x => x.KitchenStationName));
        Assert.All(order.Tickets, t => Assert.Equal($"{order.Code}-1", t.Number));

        // 400 + 40 + 57.20 for the momo, 60 + 7.80 for the coke: the vendor's own line figures.
        Assert.Equal(460m, order.Amount);
        Assert.Equal(40m, order.ServiceCharge);
        Assert.Equal(65m, order.Vat);
        Assert.Equal(565m, order.Total);
    }

    [Fact]
    public async Task Adding_more_sends_only_the_change_to_the_kitchen()
    {
        var r = await PosTestRestaurant.CreateAsync();
        var order = await r.SeatAsync(r.T1, [r.Momo(2), r.Coke(1)]);

        order = await r.AddAsync(
            order.Id,
            newItems: [r.Momo(1, "extra chutney")],
            moreOf: [PosTestRestaurant.Line(order, "Coke 250ml", 1m)]);

        var second = order.Tickets.Where(x => x.SendNumber == 2).ToList();
        Assert.Equal(2, second.Count);
        Assert.Equal(1m, Assert.Single(second.Single(x => x.KitchenStationName == "Kitchen").Lines).Quantity);
        var coke = Assert.Single(second.Single(x => x.KitchenStationName == KitchenStation.DefaultName).Lines);
        Assert.Equal(("Coke 250ml", 1m), (coke.ProductName, coke.Quantity));

        Assert.Equal(2m, order.Lines.Single(x => x.ProductName == "Coke 250ml").Quantity);
        Assert.Equal(3, order.Lines.Count);
    }

    [Fact]
    public async Task Serving_is_partial_and_never_more_than_is_outstanding()
    {
        var r = await PosTestRestaurant.CreateAsync();
        var order = await r.SeatAsync(r.T1, [r.Momo(2)]);

        order = await r.ServeAsync(order.Id, PosTestRestaurant.Line(order, "Chicken Momo", 1m));
        var momo = order.Lines.Single();
        Assert.Equal((1m, 1m), (momo.Served, momo.Outstanding));

        await Assert.ThrowsAsync<ConflictException>(
            () => r.ServeAsync(order.Id, PosTestRestaurant.Line(order, "Chicken Momo", 2m)));
    }

    [Fact]
    public async Task A_discard_cancels_to_the_kitchen_with_its_reason_and_is_audited_as_a_void()
    {
        var r = await PosTestRestaurant.CreateAsync();
        var order = await r.SeatAsync(r.T1, [r.Momo(2)]);

        order = await r.DiscardAsync(order.Id, "Guest changed their mind", PosTestRestaurant.Line(order, "Chicken Momo", 1m));

        var cancellation = order.Tickets.Single(x => x.IsCancellation);
        Assert.Equal("Guest changed their mind", cancellation.Reason);
        Assert.Equal(-1m, Assert.Single(cancellation.Lines).Quantity);
        var momo = order.Lines.Single();
        Assert.Equal((2m, 1m, 1m), (momo.Ordered, momo.Discarded, momo.Quantity));

        // The pipeline's half: Admin-only key, and a Void-prefixed name, so AuditBehavior writes its row.
        var command = new VoidPosOrderItemsCommand(r.OrganizationId, order.Id, [], "x");
        Assert.Equal(PermissionKeys.PosOrderVoid, command.PermissionKey);
        Assert.Equal((DocumentType.PosOrder, order.Id), (command.AuditDocumentType, command.AuditDocumentId));
        Assert.StartsWith("Void", nameof(VoidPosOrderItemsCommand), StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_discard_without_a_reason_is_a_400_naming_the_field()
    {
        var validator = new VoidPosOrderItemsCommandValidator();

        var result = await validator.ValidateAsync(new VoidPosOrderItemsCommand(
            Guid.NewGuid(), Guid.NewGuid(), [new PosOrderLineQuantityInput(Guid.NewGuid(), 1m)], " "));

        Assert.Contains(result.Errors, e => e.PropertyName == nameof(VoidPosOrderItemsCommand.Reason));
    }

    [Fact]
    public async Task One_open_order_per_table_and_a_voided_one_frees_it()
    {
        var r = await PosTestRestaurant.CreateAsync();
        var order = await r.SeatAsync(r.T1, [r.Momo(1)]);

        var taken = await Assert.ThrowsAsync<ConflictException>(() => r.SeatAsync(r.T1, [r.Coke(1)]));
        Assert.Contains(order.Code, taken.Message, StringComparison.Ordinal);

        var voided = await r.VoidAsync(order.Id);
        Assert.Equal(PosOrderStatus.Voided, voided.Status);
        Assert.All(voided.Lines, l => Assert.Equal(0m, l.Quantity));

        var next = await r.SeatAsync(r.T1, [r.Coke(1)]);
        Assert.NotEqual(order.Code, next.Code);
    }

    [Fact]
    public async Task An_order_moves_to_a_free_table_and_never_onto_a_taken_one()
    {
        var r = await PosTestRestaurant.CreateAsync();
        var first = await r.SeatAsync(r.T1, [r.Momo(1)]);
        await r.SeatAsync(r.T2, [r.Coke(1)]);

        var handler = new UpdatePosOrderCommandHandler(r.Till.Db, r.CurrentUser());

        await Assert.ThrowsAsync<ConflictException>(() => handler.Handle(
            new UpdatePosOrderCommand(r.OrganizationId, first.Id, r.T2, 2, null), CancellationToken.None));

        var moved = await handler.Handle(
            new UpdatePosOrderCommand(r.OrganizationId, first.Id, r.R1, 3, null), CancellationToken.None);
        Assert.Equal(("R1", "Rooftop", 3), (moved.TableName, moved.AreaName, moved.Covers));
    }

    [Fact]
    public async Task Take_away_and_delivery_carry_no_service_charge()
    {
        var r = await PosTestRestaurant.CreateAsync();

        var takeAway = await r.OpenAsync(PosTab.TakeAway, [r.Momo(1)]);

        Assert.Equal(0m, takeAway.Lines.Single().ServiceChargeRate);
        Assert.Equal(226m, takeAway.Total);
        Assert.Null(takeAway.TableId);
    }

    [Fact]
    public async Task A_delivery_goes_to_a_named_customer_and_never_the_walk_in()
    {
        var r = await PosTestRestaurant.CreateAsync();
        var customer = Contact.Create(
            r.OrganizationId, ContactType.Customer, "Acme Retail", "C-0001", "Lazimpat", null, null, null, null, 0m);
        r.Till.Db.Contacts.Add(customer);
        await r.Till.Db.SaveChangesAsync();

        await Assert.ThrowsAsync<ValidationException>(() => r.OpenAsync(PosTab.Delivery, [r.Momo(1)], r.Till.WalkInId));

        var delivery = await r.OpenAsync(PosTab.Delivery, [r.Momo(1)], customer.Id);
        Assert.Equal("Acme Retail", delivery.ContactName);
        Assert.Equal(0m, delivery.ServiceCharge);
    }

    [Fact]
    public async Task An_order_reserves_nothing_and_posts_nothing()
    {
        var r = await PosTestRestaurant.CreateAsync();
        var gl = await r.Till.Db.GlJournalEntries.CountAsync();
        var movements = await r.Till.Db.StockMovements.CountAsync();

        var order = await r.SeatAsync(r.T1, [r.Momo(2), r.Coke(3)]);
        order = await r.AddAsync(order.Id, moreOf: [PosTestRestaurant.Line(order, "Coke 250ml", 1m)]);
        order = await r.ServeAsync(order.Id, PosTestRestaurant.Line(order, "Coke 250ml", 4m));
        await r.DiscardAsync(order.Id, "Dropped", PosTestRestaurant.Line(order, "Chicken Momo", 1m));

        Assert.Equal(gl, await r.Till.Db.GlJournalEntries.CountAsync());
        Assert.Equal(movements, await r.Till.Db.StockMovements.CountAsync());
        Assert.Equal(0, await r.Till.Db.Invoices.CountAsync(x => x.Channel == Domain.Sales.SalesChannel.Pos));
    }

    [Fact]
    public async Task A_waiter_takes_orders_only_where_they_may_raise_the_invoice()
    {
        // Pos.Order.Operate is organization-wide; Sales.Invoice.Create at the location is the boundary.
        var r = await PosTestRestaurant.CreateAsync(
            [.. PosTestRestaurant.WaiterKeys.Where(k => k != PermissionKeys.InvoiceCreate)]);

        var refused = await Assert.ThrowsAsync<ForbiddenException>(() => r.SeatAsync(r.T1, [r.Momo(1)]));
        Assert.Contains(PermissionKeys.InvoiceCreate, refused.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_retail_till_takes_no_restaurant_orders()
    {
        var till = await PosTestTill.CreateAsync(PosTestRestaurant.WaiterKeys);

        await Assert.ThrowsAsync<ConflictException>(() =>
            new CreatePosOrderCommandHandler(till.Db, till.Seed.NumberGenerator, till.CurrentUser()).Handle(
                new CreatePosOrderCommand(till.OrganizationId, till.Location.Id, PosTab.TakeAway, null, 0, null,
                    [new PosOrderItemInput(till.MomoId, 1m, null, null)]),
                CancellationToken.None));
    }

    [Fact]
    public async Task A_product_not_for_sale_here_cannot_be_ordered()
    {
        var r = await PosTestRestaurant.CreateAsync();
        var coke = await r.Till.Db.Products.SingleAsync(x => x.Id == r.Till.CokeId);
        coke.Update(coke.Name, coke.CategoryId, coke.PrimaryUnitId, coke.HsCode, availableForSale: false,
            coke.SellingPrice, coke.PurchasePrice, coke.VatRate, coke.ReOrderLevel, coke.TrackInventory, coke.IsActive);
        await r.Till.Db.SaveChangesAsync();

        var refused = await Assert.ThrowsAsync<ConflictException>(() => r.SeatAsync(r.T1, [r.Coke(1)]));
        Assert.Contains("Coke 250ml", refused.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_floor_shows_which_tables_are_taken_and_the_open_parcels()
    {
        var r = await PosTestRestaurant.CreateAsync();
        var seated = await r.SeatAsync(r.T2, [r.Momo(1)]);
        var parcel = await r.OpenAsync(PosTab.TakeAway, [r.Coke(2)]);

        var floor = await new GetPosRestaurantQueryHandler(r.Till.Db, r.CurrentUser()).Handle(
            new GetPosRestaurantQuery(r.OrganizationId, r.LocationId), CancellationToken.None);

        Assert.Equal(["Ground Floor", "Rooftop"], floor.Areas.Select(x => x.Name));
        var tables = floor.Areas.SelectMany(a => a.Tables).ToDictionary(t => t.Name);
        Assert.Null(tables["T1"].Order);
        Assert.Equal(seated.Code, tables["T2"].Order!.Code);
        Assert.Equal([seated.Id, parcel.Id], floor.Orders.Select(x => x.Id));
        Assert.Equal([PosTab.DineIn, PosTab.TakeAway, PosTab.Delivery], floor.AvailableTabs);
        Assert.True(floor.CanVoid);
    }

    [Fact]
    public async Task A_table_name_is_unique_across_the_location_and_an_occupied_table_stays_active()
    {
        var r = await PosTestRestaurant.CreateAsync();
        var floor = await r.FloorAsync();
        var rooftop = floor.Areas.Single(x => x.Name == "Rooftop");
        var r1 = rooftop.Tables.Single();
        var handler = new SavePosAreaLayoutCommandHandler(r.Till.Db);

        // T1 already stands on the Ground Floor.
        await Assert.ThrowsAsync<ValidationException>(() => handler.Handle(
            new SavePosAreaLayoutCommand(r.OrganizationId, rooftop.Id,
            [
                new PosTableLayoutInput(r1.Id, r1.Name, 6, PosTableShape.Rectangle, 100, 100, 250, 100, true),
                new PosTableLayoutInput(null, "T1", 4, PosTableShape.Circle, 500, 100, 100, 100, true),
            ]),
            CancellationToken.None));

        // Leaving a table out is not a delete.
        await Assert.ThrowsAsync<ValidationException>(() => handler.Handle(
            new SavePosAreaLayoutCommand(r.OrganizationId, rooftop.Id, []), CancellationToken.None));

        await r.SeatAsync(r.R1, [r.Momo(1)]);
        await Assert.ThrowsAsync<ConflictException>(() => handler.Handle(
            new SavePosAreaLayoutCommand(r.OrganizationId, rooftop.Id,
                [new PosTableLayoutInput(r1.Id, r1.Name, 6, PosTableShape.Rectangle, 100, 100, 250, 100, false)]),
            CancellationToken.None));
        await Assert.ThrowsAsync<ConflictException>(() => new UpdatePosAreaCommandHandler(r.Till.Db).Handle(
            new UpdatePosAreaCommand(r.OrganizationId, rooftop.Id, "Rooftop", false), CancellationToken.None));

        var after = await r.FloorAsync();
        Assert.True(after.Areas.Single(x => x.Name == "Rooftop").Tables.Single().IsOccupied);
    }

    [Fact]
    public async Task Kitchen_stations_route_tickets_and_keep_their_products()
    {
        var r = await PosTestRestaurant.CreateAsync();
        var bar = (await new CreateKitchenStationCommandHandler(r.Till.Db).Handle(
            new CreateKitchenStationCommand(r.OrganizationId, "Bar"), CancellationToken.None))
            .Single(x => x.Name == "Bar");
        var stations = await new SetKitchenStationProductsCommandHandler(r.Till.Db).Handle(
            new SetKitchenStationProductsCommand(r.OrganizationId, bar.Id, [r.Till.CokeId]), CancellationToken.None);
        Assert.Equal(["Coke 250ml"], stations.Single(x => x.Name == "Bar").Products.Select(x => x.Name));

        var order = await r.SeatAsync(r.T1, [r.Momo(1), r.Coke(1)]);
        Assert.Equal(["Bar", "Kitchen"], order.Tickets.Select(x => x.KitchenStationName).Order());

        // A station still sent products cannot be switched off; once emptied it can.
        var update = new UpdateKitchenStationCommandHandler(r.Till.Db);
        await Assert.ThrowsAsync<ConflictException>(() => update.Handle(
            new UpdateKitchenStationCommand(r.OrganizationId, bar.Id, "Bar", false), CancellationToken.None));
        await new SetKitchenStationProductsCommandHandler(r.Till.Db).Handle(
            new SetKitchenStationProductsCommand(r.OrganizationId, bar.Id, []), CancellationToken.None);
        await update.Handle(new UpdateKitchenStationCommand(r.OrganizationId, bar.Id, "Bar", false), CancellationToken.None);

        // The line keeps the station it was first sent to, so more Coke still goes to the Bar.
        order = await r.AddAsync(order.Id, moreOf: [PosTestRestaurant.Line(order, "Coke 250ml", 1m)]);
        Assert.Equal("Bar", order.Tickets.Single(x => x.SendNumber == 2).KitchenStationName);
    }

    [Fact]
    public async Task A_ticket_print_is_counted_so_the_second_is_a_reprint()
    {
        var r = await PosTestRestaurant.CreateAsync();
        var order = await r.SeatAsync(r.T1, [r.Momo(1)]);
        var ticket = order.Tickets.Single();
        var handler = new PrintPosKitchenTicketCommandHandler(r.Till.Db, r.CurrentUser());

        var first = await handler.Handle(new PrintPosKitchenTicketCommand(r.OrganizationId, order.Id, ticket.Id), CancellationToken.None);
        var second = await handler.Handle(new PrintPosKitchenTicketCommand(r.OrganizationId, order.Id, ticket.Id), CancellationToken.None);

        Assert.Equal((1, 2), (first.PrintNumber, second.PrintNumber));
        await Assert.ThrowsAsync<NotFoundException>(() => handler.Handle(
            new PrintPosKitchenTicketCommand(r.OrganizationId, order.Id, Guid.NewGuid()), CancellationToken.None));
    }

    [Fact]
    public async Task The_ERP_list_shows_every_order_with_its_lines_and_filters_by_status()
    {
        var r = await PosTestRestaurant.CreateAsync();
        var open = await r.SeatAsync(r.T1, [r.Momo(2)]);
        var voided = await r.SeatAsync(r.T2, [r.Coke(1)]);
        await r.VoidAsync(voided.Id, "Wrong table");
        var handler = new ListPosOrdersQueryHandler(r.Till.Db, r.CurrentUser());

        var all = await handler.Handle(new ListPosOrdersQuery(r.OrganizationId), CancellationToken.None);
        Assert.Equal([voided.Id, open.Id], all.Items.Select(x => x.Id));
        Assert.Equal(2m, all.Items.Single(x => x.Id == open.Id).Lines.Single().Quantity);
        Assert.Equal("Wrong table", all.Items.Single(x => x.Id == voided.Id).VoidReason);

        var openOnly = await handler.Handle(
            new ListPosOrdersQuery(r.OrganizationId, Status: PosOrderStatus.Open), CancellationToken.None);
        Assert.Equal([open.Id], openOnly.Items.Select(x => x.Id));

        var search = await handler.Handle(
            new ListPosOrdersQuery(r.OrganizationId, Search: voided.Code), CancellationToken.None);
        Assert.Equal([voided.Id], search.Items.Select(x => x.Id));
    }

    [Fact]
    public async Task Reading_an_order_shows_it_voided_and_acting_on_it_is_refused()
    {
        var r = await PosTestRestaurant.CreateAsync();
        var order = await r.SeatAsync(r.T1, [r.Momo(1)]);
        await r.VoidAsync(order.Id);

        var read = await new GetPosOrderQueryHandler(r.Till.Db, r.CurrentUser()).Handle(
            new GetPosOrderQuery(r.OrganizationId, order.Id), CancellationToken.None);
        Assert.Equal(PosOrderStatus.Voided, read.Status);

        await Assert.ThrowsAsync<ConflictException>(() => r.AddAsync(order.Id, [r.Coke(1)]));
        await Assert.ThrowsAsync<ConflictException>(() => new VoidPosOrderCommandHandler(r.Till.Db, r.CurrentUser())
            .Handle(new VoidPosOrderCommand(r.OrganizationId, order.Id, "again"), CancellationToken.None));
    }

    [Fact]
    public async Task A_delivery_sale_at_the_retail_till_carries_no_service_charge_either()
    {
        // The rule is PosServiceCharge's, shared by the sale engine: phase 61 charged it on every tab.
        var till = await PosTestTill.CreateAsync();
        var session = await till.OpenAsync();

        var sale = await till.SaleHandler().Handle(
            new CreatePosSaleCommand(
                till.OrganizationId, session.Id, till.Location.Id, [till.Momo(1)], [till.Cash(226m)], 0m, null,
                till.Seed.WarehouseId, OrderType: PosTab.Delivery),
            CancellationToken.None);

        Assert.Equal(0m, sale.ServiceCharge);
        Assert.Equal(226m, sale.GrandTotal);
    }
}
