using ErpApp.Application.Inventory;
using ErpApp.Application.Inventory.Commands.ApproveInventoryAdjustment;
using ErpApp.Application.Inventory.Commands.ApproveWarehouseTransfer;
using ErpApp.Application.Inventory.Commands.CreateInventoryAdjustment;
using ErpApp.Application.Inventory.Commands.CreateWarehouseTransfer;
using ErpApp.Application.Inventory.Commands.UpdateInventoryAdjustment;
using ErpApp.Application.Inventory.Commands.UpdateWarehouseTransfer;
using ErpApp.Application.Inventory.Queries.GetInventoryAdjustment;
using ErpApp.Application.Inventory.Queries.GetWarehouseTransfer;
using ErpApp.Application.Inventory.Queries.InventoryLedger;
using ErpApp.Application.Inventory.Queries.ListInventoryAdjustments;
using ErpApp.Application.Inventory.Queries.ListWarehouseTransfers;
using ErpApp.Application.Inventory.Queries.ProductStockPosition;
using ErpApp.Domain.Inventory;
using MediatR;

namespace ErpApp.Api.Endpoints;

public static class InventoryEndpoints
{
    public static void MapInventoryEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/organizations/{organizationId:guid}")
            .WithTags("Inventory")
            .RequireAuthorization();

        MapWarehouseTransferEndpoints(group);
        MapInventoryAdjustmentEndpoints(group);
        MapReportEndpoints(group);
    }

    private static void MapWarehouseTransferEndpoints(RouteGroupBuilder group)
    {
        group.MapGet("/warehouse-transfers", async (
            Guid organizationId, WarehouseTransferStatus? status, ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(new ListWarehouseTransfersQuery(organizationId, status), ct);
            return Results.Ok(result);
        });

        group.MapGet("/warehouse-transfers/{id:guid}", async (
            Guid organizationId, Guid id, ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(new GetWarehouseTransferQuery(organizationId, id), ct);
            return Results.Ok(result);
        });

        group.MapPost("/warehouse-transfers", async (
            Guid organizationId, WarehouseTransferRequest request, ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(
                new CreateWarehouseTransferCommand(
                    organizationId, request.FromWarehouseId, request.ToWarehouseId, request.Date, request.Reference, request.Lines),
                ct);
            return Results.Created($"/api/organizations/{organizationId}/warehouse-transfers/{result.Id}", result);
        });

        group.MapPut("/warehouse-transfers/{id:guid}", async (
            Guid organizationId, Guid id, WarehouseTransferRequest request, ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(
                new UpdateWarehouseTransferCommand(
                    organizationId, id, request.FromWarehouseId, request.ToWarehouseId, request.Date, request.Reference, request.Lines),
                ct);
            return Results.Ok(result);
        });

        group.MapPost("/warehouse-transfers/{id:guid}/approve", async (
            Guid organizationId, Guid id, ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(new ApproveWarehouseTransferCommand(organizationId, id), ct);
            return Results.Ok(result);
        });
    }

    private static void MapInventoryAdjustmentEndpoints(RouteGroupBuilder group)
    {
        group.MapGet("/inventory-adjustments", async (
            Guid organizationId, InventoryAdjustmentStatus? status, ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(new ListInventoryAdjustmentsQuery(organizationId, status), ct);
            return Results.Ok(result);
        });

        group.MapGet("/inventory-adjustments/{id:guid}", async (
            Guid organizationId, Guid id, ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(new GetInventoryAdjustmentQuery(organizationId, id), ct);
            return Results.Ok(result);
        });

        group.MapPost("/inventory-adjustments", async (
            Guid organizationId, InventoryAdjustmentRequest request, ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(
                new CreateInventoryAdjustmentCommand(organizationId, request.WarehouseId, request.Date, request.Reference, request.Lines),
                ct);
            return Results.Created($"/api/organizations/{organizationId}/inventory-adjustments/{result.Id}", result);
        });

        group.MapPut("/inventory-adjustments/{id:guid}", async (
            Guid organizationId, Guid id, InventoryAdjustmentRequest request, ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(
                new UpdateInventoryAdjustmentCommand(
                    organizationId, id, request.WarehouseId, request.Date, request.Reference, request.Lines),
                ct);
            return Results.Ok(result);
        });

        group.MapPost("/inventory-adjustments/{id:guid}/approve", async (
            Guid organizationId, Guid id, ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(new ApproveInventoryAdjustmentCommand(organizationId, id), ct);
            return Results.Ok(result);
        });
    }

    private static void MapReportEndpoints(RouteGroupBuilder group)
    {
        group.MapGet("/inventory/stock-position", async (
            Guid organizationId, Guid? productId, Guid? warehouseId, ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(new ProductStockPositionQuery(organizationId, productId, warehouseId), ct);
            return Results.Ok(result);
        });

        group.MapGet("/inventory/ledger", async (
            Guid organizationId, Guid productId, Guid warehouseId, ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(new InventoryLedgerQuery(organizationId, productId, warehouseId), ct);
            return Results.Ok(result);
        });
    }

    private sealed record WarehouseTransferRequest(
        Guid FromWarehouseId, Guid ToWarehouseId, DateOnly Date, string? Reference, IReadOnlyList<WarehouseTransferLineInput> Lines);

    private sealed record InventoryAdjustmentRequest(
        Guid WarehouseId, DateOnly Date, string? Reference, IReadOnlyList<InventoryAdjustmentLineInput> Lines);
}
