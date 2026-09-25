using ErpApp.Application.Common.Pagination;
using ErpApp.Application.Purchasing;
using ErpApp.Application.Purchasing.Commands.ApproveGoodsReceivedNote;
using ErpApp.Application.Purchasing.Commands.CreateGoodsReceivedNote;
using ErpApp.Application.Purchasing.Commands.UpdateGoodsReceivedNote;
using ErpApp.Application.Purchasing.Commands.VoidGoodsReceivedNote;
using ErpApp.Application.Purchasing.Queries.GetGoodsReceivedNote;
using ErpApp.Application.Purchasing.Queries.GetGoodsReceivedNoteConversionTemplate;
using ErpApp.Application.Purchasing.Queries.ListGoodsReceivedNotes;
using ErpApp.Application.Sales;
using ErpApp.Application.Sales.Commands.ApproveDeliveryNote;
using ErpApp.Application.Sales.Commands.CreateDeliveryNote;
using ErpApp.Application.Sales.Commands.UpdateDeliveryNote;
using ErpApp.Application.Sales.Commands.VoidDeliveryNote;
using ErpApp.Application.Sales.Queries.GetDeliveryNote;
using ErpApp.Application.Sales.Queries.GetDeliveryNoteConversionTemplate;
using ErpApp.Application.Sales.Queries.ListDeliveryNotes;
using ErpApp.Domain.Common;
using ErpApp.Domain.Purchasing;
using ErpApp.Domain.Sales;
using MediatR;

namespace ErpApp.Api.Endpoints;

/// <summary>
/// Phase 58 -- Goods Received Notes and Delivery Notes, the two physical-movement documents, and
/// the two conversions into them. One file for the pair rather than a tail on the Purchasing and
/// Sales files: they are one feature, gated by one setting, and they read one ledger.
/// </summary>
public static class PhysicalMovementEndpoints
{
    public static void MapPhysicalMovementEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/organizations/{organizationId:guid}")
            .WithTags("Physical movement")
            .RequireAuthorization();

        MapGoodsReceivedNoteEndpoints(group);
        MapDeliveryNoteEndpoints(group);
    }

    private static void MapGoodsReceivedNoteEndpoints(RouteGroupBuilder group)
    {
        group.MapGet("/goods-received-notes", async (
            Guid organizationId, GoodsReceivedNoteStatus? status, int? page, int? pageSize, string? search, DateOnly? fromDate,
            DateOnly? toDate, Guid? locationId, string? sort, ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(
                new ListGoodsReceivedNotesQuery(
                    organizationId, status, page ?? 1, pageSize ?? PagingDefaults.DefaultPageSize, search, fromDate, toDate,
                    locationId, sort), ct);
            return Results.Ok(result);
        });

        group.MapGet("/goods-received-notes/{id:guid}", async (
            Guid organizationId, Guid id, ISender sender, CancellationToken ct) =>
            Results.Ok(await sender.Send(new GetGoodsReceivedNoteQuery(organizationId, id), ct)));

        group.MapPost("/goods-received-notes", async (
            Guid organizationId, GoodsReceivedNoteRequest request, ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(
                new CreateGoodsReceivedNoteCommand(
                    organizationId, request.ContactId, request.WarehouseId, request.Date, request.Reference, request.TrackingNo,
                    request.Lines, request.DiscountPct, request.ReferrerType, request.ReferrerId)
                {
                    CurrencyCode = request.CurrencyCode, ExchangeRate = request.ExchangeRate, LocationId = request.LocationId,
                },
                ct);
            return Results.Created($"/api/organizations/{organizationId}/goods-received-notes/{result.Id}", result);
        });

        group.MapPut("/goods-received-notes/{id:guid}", async (
            Guid organizationId, Guid id, GoodsReceivedNoteRequest request, ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(
                new UpdateGoodsReceivedNoteCommand(
                    organizationId, id, request.ContactId, request.WarehouseId, request.Date, request.Reference,
                    request.TrackingNo, request.Lines, request.DiscountPct)
                {
                    CurrencyCode = request.CurrencyCode, ExchangeRate = request.ExchangeRate, LocationId = request.LocationId,
                },
                ct);
            return Results.Ok(result);
        });

        group.MapPost("/goods-received-notes/{id:guid}/approve", async (
            Guid organizationId, Guid id, ISender sender, CancellationToken ct) =>
            Results.Ok(await sender.Send(new ApproveGoodsReceivedNoteCommand(organizationId, id), ct)));

        group.MapPost("/goods-received-notes/{id:guid}/void", async (
            Guid organizationId, Guid id, ISender sender, CancellationToken ct) =>
            Results.Ok(await sender.Send(new VoidGoodsReceivedNoteCommand(organizationId, id), ct)));

        group.MapGet("/purchase-orders/{purchaseOrderId:guid}/goods-received-note-conversion-template", async (
            Guid organizationId, Guid purchaseOrderId, ISender sender, CancellationToken ct) =>
            Results.Ok(await sender.Send(new GetGoodsReceivedNoteConversionTemplateQuery(organizationId, purchaseOrderId), ct)));
    }

    private static void MapDeliveryNoteEndpoints(RouteGroupBuilder group)
    {
        group.MapGet("/delivery-notes", async (
            Guid organizationId, DeliveryNoteStatus? status, int? page, int? pageSize, string? search, DateOnly? fromDate,
            DateOnly? toDate, Guid? locationId, string? sort, ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(
                new ListDeliveryNotesQuery(
                    organizationId, status, page ?? 1, pageSize ?? PagingDefaults.DefaultPageSize, search, fromDate, toDate,
                    locationId, sort), ct);
            return Results.Ok(result);
        });

        group.MapGet("/delivery-notes/{id:guid}", async (
            Guid organizationId, Guid id, ISender sender, CancellationToken ct) =>
            Results.Ok(await sender.Send(new GetDeliveryNoteQuery(organizationId, id), ct)));

        group.MapPost("/delivery-notes", async (
            Guid organizationId, DeliveryNoteRequest request, ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(
                new CreateDeliveryNoteCommand(
                    organizationId, request.ContactId, request.WarehouseId, request.Date, request.ExpectedDeliveryDate,
                    request.Reference, request.TrackingNo, request.ShippingAddress, request.Lines, request.DiscountPct,
                    request.ReferrerType, request.ReferrerId, request.Terms)
                {
                    CurrencyCode = request.CurrencyCode, ExchangeRate = request.ExchangeRate, LocationId = request.LocationId,
                },
                ct);
            return Results.Created($"/api/organizations/{organizationId}/delivery-notes/{result.Id}", result);
        });

        group.MapPut("/delivery-notes/{id:guid}", async (
            Guid organizationId, Guid id, DeliveryNoteRequest request, ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(
                new UpdateDeliveryNoteCommand(
                    organizationId, id, request.ContactId, request.WarehouseId, request.Date, request.ExpectedDeliveryDate,
                    request.Reference, request.TrackingNo, request.ShippingAddress, request.Lines, request.DiscountPct,
                    request.Terms)
                {
                    CurrencyCode = request.CurrencyCode, ExchangeRate = request.ExchangeRate, LocationId = request.LocationId,
                },
                ct);
            return Results.Ok(result);
        });

        group.MapPost("/delivery-notes/{id:guid}/approve", async (
            Guid organizationId, Guid id, ApproveDeliveryNoteRequest? request, ISender sender, CancellationToken ct) =>
            Results.Ok(await sender.Send(
                new ApproveDeliveryNoteCommand(organizationId, id, request?.OverrideWarning ?? false), ct)));

        group.MapPost("/delivery-notes/{id:guid}/void", async (
            Guid organizationId, Guid id, ISender sender, CancellationToken ct) =>
            Results.Ok(await sender.Send(new VoidDeliveryNoteCommand(organizationId, id), ct)));

        group.MapGet("/sales-orders/{salesOrderId:guid}/delivery-note-conversion-template", async (
            Guid organizationId, Guid salesOrderId, ISender sender, CancellationToken ct) =>
            Results.Ok(await sender.Send(new GetDeliveryNoteConversionTemplateQuery(organizationId, salesOrderId), ct)));
    }

    /// <summary>Every optional field lives on the request record itself, not only on the command --
    /// phase 27b's rule: a trailing optional parameter added to a command alone binds to null
    /// forever while every test passes.</summary>
    private sealed record GoodsReceivedNoteRequest(
        Guid ContactId, Guid WarehouseId, DateOnly Date, string? Reference, string? TrackingNo,
        IReadOnlyList<GoodsReceivedNoteLineInput> Lines, decimal DiscountPct = 0,
        DocumentType? ReferrerType = null, Guid? ReferrerId = null,
        string? CurrencyCode = null, decimal? ExchangeRate = null, Guid? LocationId = null);

    /// <inheritdoc cref="GoodsReceivedNoteRequest"/>
    private sealed record DeliveryNoteRequest(
        Guid ContactId, Guid WarehouseId, DateOnly Date, DateOnly ExpectedDeliveryDate, string? Reference, string? TrackingNo,
        string? ShippingAddress, IReadOnlyList<DeliveryNoteLineInput> Lines, decimal DiscountPct = 0,
        DocumentType? ReferrerType = null, Guid? ReferrerId = null, string? Terms = null,
        string? CurrencyCode = null, decimal? ExchangeRate = null, Guid? LocationId = null);

    /// <summary>The Invoice's Warn-and-continue shape: an optional body so a plain POST still works.</summary>
    private sealed record ApproveDeliveryNoteRequest(bool OverrideWarning = false);
}
