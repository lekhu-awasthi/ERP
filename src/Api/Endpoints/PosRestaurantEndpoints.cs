using ErpApp.Application.Common.Pagination;
using ErpApp.Application.Pos.Commands.AddPosOrderItems;
using ErpApp.Application.Pos.Commands.CreateKitchenStation;
using ErpApp.Application.Pos.Commands.CreatePosArea;
using ErpApp.Application.Pos.Commands.CreatePosOrder;
using ErpApp.Application.Pos.Commands.PrintPosKitchenTicket;
using ErpApp.Application.Pos.Commands.SavePosAreaLayout;
using ErpApp.Application.Pos.Commands.ServePosOrderItems;
using ErpApp.Application.Pos.Commands.SetKitchenStationProducts;
using ErpApp.Application.Pos.Commands.UpdateKitchenStation;
using ErpApp.Application.Pos.Commands.UpdatePosArea;
using ErpApp.Application.Pos.Commands.UpdatePosOrder;
using ErpApp.Application.Pos.Commands.VoidPosOrder;
using ErpApp.Application.Pos.Commands.VoidPosOrderItems;
using ErpApp.Application.Pos.Queries.GetPosFloorPlan;
using ErpApp.Application.Pos.Queries.GetPosOrder;
using ErpApp.Application.Pos.Queries.GetPosRestaurant;
using ErpApp.Application.Pos.Queries.ListKitchenStations;
using ErpApp.Application.Pos.Queries.ListPosOrderProducts;
using ErpApp.Application.Pos.Queries.ListPosOrders;
using ErpApp.Application.Pos.Queries.ListPosProducts;
using ErpApp.Application.Pos.Restaurant;
using ErpApp.Domain.Pos;
using MediatR;

namespace ErpApp.Api.Endpoints;

/// <summary>
/// Phase 64 -- the restaurant: the floor plan and kitchen stations (configuration), and the till's
/// orders and kitchen tickets, under the same <c>/pos</c> prefix as the rest of the till. Shapes are in
/// <c>docs/e2e-recipes.md</c>.
/// </summary>
public static class PosRestaurantEndpoints
{
    public static void MapPosRestaurantEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/organizations/{organizationId:guid}/pos")
            .WithTags("Point of sale -- restaurant")
            .RequireAuthorization();

        // ---- Configuration: the floor plan (Pos.FloorPlan.Manage) and kitchen stations (Pos.Settings.Manage)

        group.MapGet("/locations/{locationId:guid}/floor-plan", async (
            Guid organizationId, Guid locationId, ISender sender, CancellationToken ct) =>
            Results.Ok(await sender.Send(new GetPosFloorPlanQuery(organizationId, locationId), ct)));

        group.MapPost("/locations/{locationId:guid}/areas", async (
            Guid organizationId, Guid locationId, CreatePosAreaRequest request, ISender sender, CancellationToken ct) =>
            Results.Ok(await sender.Send(new CreatePosAreaCommand(organizationId, locationId, request.Name ?? ""), ct)));

        group.MapPut("/areas/{areaId:guid}", async (
            Guid organizationId, Guid areaId, UpdatePosAreaRequest request, ISender sender, CancellationToken ct) =>
            Results.Ok(await sender.Send(
                new UpdatePosAreaCommand(organizationId, areaId, request.Name ?? "", request.IsActive), ct)));

        // The whole layout in the body, as the vendor's Save Changes sends it.
        group.MapPut("/areas/{areaId:guid}/layout", async (
            Guid organizationId, Guid areaId, SavePosAreaLayoutRequest request, ISender sender, CancellationToken ct) =>
            Results.Ok(await sender.Send(new SavePosAreaLayoutCommand(organizationId, areaId, request.Tables ?? []), ct)));

        group.MapGet("/kitchen-stations", async (Guid organizationId, ISender sender, CancellationToken ct) =>
            Results.Ok(await sender.Send(new ListKitchenStationsQuery(organizationId), ct)));

        group.MapPost("/kitchen-stations", async (
            Guid organizationId, CreateKitchenStationRequest request, ISender sender, CancellationToken ct) =>
            Results.Ok(await sender.Send(new CreateKitchenStationCommand(organizationId, request.Name ?? ""), ct)));

        group.MapPut("/kitchen-stations/{stationId:guid}", async (
            Guid organizationId, Guid stationId, UpdateKitchenStationRequest request, ISender sender,
            CancellationToken ct) =>
            Results.Ok(await sender.Send(
                new UpdateKitchenStationCommand(organizationId, stationId, request.Name ?? "", request.IsActive), ct)));

        // A PUT with the list in the body (phase 38's array gotcha is about a POST meant to read the query).
        group.MapPut("/kitchen-stations/{stationId:guid}/products", async (
            Guid organizationId, Guid stationId, SetKitchenStationProductsRequest request, ISender sender,
            CancellationToken ct) =>
            Results.Ok(await sender.Send(
                new SetKitchenStationProductsCommand(organizationId, stationId, request.ProductIds ?? []), ct)));

        // ---- The restaurant till (Pos.Order.Operate; discards Pos.Order.Void) ----------------------

        group.MapGet("/restaurants/{locationId:guid}", async (
            Guid organizationId, Guid locationId, ISender sender, CancellationToken ct) =>
            Results.Ok(await sender.Send(new GetPosRestaurantQuery(organizationId, locationId), ct)));

        group.MapGet("/restaurants/{locationId:guid}/products", async (
            Guid organizationId, Guid locationId, string? search, string? code, Guid? categoryId, int? page,
            int? pageSize, ISender sender, CancellationToken ct) =>
            Results.Ok(await sender.Send(
                new ListPosOrderProductsQuery(
                    organizationId, locationId, search, code, categoryId, page ?? 1,
                    pageSize ?? ListPosProductsQuery.DefaultPageSize),
                ct)));

        group.MapPost("/orders", async (
            Guid organizationId, CreatePosOrderRequest request, ISender sender, CancellationToken ct) =>
            Results.Ok(await sender.Send(
                new CreatePosOrderCommand(
                    organizationId, request.LocationId, request.OrderType, request.TableId, request.Covers,
                    request.ContactId, request.Items ?? []),
                ct)));

        group.MapGet("/orders/{orderId:guid}", async (
            Guid organizationId, Guid orderId, ISender sender, CancellationToken ct) =>
            Results.Ok(await sender.Send(new GetPosOrderQuery(organizationId, orderId), ct)));

        group.MapPut("/orders/{orderId:guid}", async (
            Guid organizationId, Guid orderId, UpdatePosOrderRequest request, ISender sender, CancellationToken ct) =>
            Results.Ok(await sender.Send(
                new UpdatePosOrderCommand(organizationId, orderId, request.TableId, request.Covers, request.ContactId),
                ct)));

        // A send: new items and more of existing lines, ticketed per station.
        group.MapPost("/orders/{orderId:guid}/items", async (
            Guid organizationId, Guid orderId, AddPosOrderItemsRequest request, ISender sender, CancellationToken ct) =>
            Results.Ok(await sender.Send(
                new AddPosOrderItemsCommand(organizationId, orderId, request.NewItems ?? [], request.MoreOf ?? []), ct)));

        group.MapPost("/orders/{orderId:guid}/serve", async (
            Guid organizationId, Guid orderId, ServePosOrderItemsRequest request, ISender sender, CancellationToken ct) =>
            Results.Ok(await sender.Send(new ServePosOrderItemsCommand(organizationId, orderId, request.Items ?? []), ct)));

        group.MapPost("/orders/{orderId:guid}/discard", async (
            Guid organizationId, Guid orderId, VoidPosOrderItemsRequest request, ISender sender, CancellationToken ct) =>
            Results.Ok(await sender.Send(
                new VoidPosOrderItemsCommand(organizationId, orderId, request.Items ?? [], request.Reason ?? ""), ct)));

        group.MapPost("/orders/{orderId:guid}/void", async (
            Guid organizationId, Guid orderId, VoidPosOrderRequest request, ISender sender, CancellationToken ct) =>
            Results.Ok(await sender.Send(new VoidPosOrderCommand(organizationId, orderId, request.Reason ?? ""), ct)));

        // A POST because every call is a printing the server counts: 1 is the original, later ones reprints.
        group.MapPost("/orders/{orderId:guid}/tickets/{ticketId:guid}/prints", async (
            Guid organizationId, Guid orderId, Guid ticketId, ISender sender, CancellationToken ct) =>
            Results.Ok(await sender.Send(new PrintPosKitchenTicketCommand(organizationId, orderId, ticketId), ct)));

        // ---- The ERP's POS Orders list (Pos.Order.View) -----------------------------------------------

        group.MapGet("/orders", async (
            Guid organizationId, PosOrderStatus? status, PosTab? orderType, int? page, int? pageSize, string? search,
            DateOnly? fromDate, DateOnly? toDate, Guid? locationId, string? sort, ISender sender, CancellationToken ct) =>
            Results.Ok(await sender.Send(
                new ListPosOrdersQuery(
                    organizationId, status, orderType, page ?? 1, pageSize ?? PagingDefaults.DefaultPageSize, search,
                    fromDate, toDate, locationId, sort),
                ct)));
    }

    // Every field each command takes, so none binds to its default in silence (phase 27b). No rate and
    // no date: the catalogue prices an order line, and an order stamps the Nepal date itself.
    private sealed record CreatePosAreaRequest(string? Name);

    private sealed record UpdatePosAreaRequest(string? Name, bool IsActive);

    private sealed record SavePosAreaLayoutRequest(IReadOnlyList<PosTableLayoutInput>? Tables);

    private sealed record CreateKitchenStationRequest(string? Name);

    private sealed record UpdateKitchenStationRequest(string? Name, bool IsActive);

    private sealed record SetKitchenStationProductsRequest(IReadOnlyList<Guid>? ProductIds);

    private sealed record CreatePosOrderRequest(
        Guid LocationId,
        PosTab OrderType,
        Guid? TableId,
        int Covers,
        Guid? ContactId,
        IReadOnlyList<PosOrderItemInput>? Items);

    private sealed record UpdatePosOrderRequest(Guid? TableId, int Covers, Guid? ContactId);

    private sealed record AddPosOrderItemsRequest(
        IReadOnlyList<PosOrderItemInput>? NewItems, IReadOnlyList<PosOrderLineQuantityInput>? MoreOf);

    private sealed record ServePosOrderItemsRequest(IReadOnlyList<PosOrderLineQuantityInput>? Items);

    private sealed record VoidPosOrderItemsRequest(IReadOnlyList<PosOrderLineQuantityInput>? Items, string? Reason);

    private sealed record VoidPosOrderRequest(string? Reason);
}
