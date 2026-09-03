using ErpApp.Api.Reports;
using ErpApp.Application.Common.Pagination;
using ErpApp.Application.Manufacturing;
using ErpApp.Application.Manufacturing.Commands.ApproveProductionJournal;
using ErpApp.Application.Manufacturing.Commands.ApproveProductionOrder;
using ErpApp.Application.Manufacturing.Commands.CreateBillOfMaterials;
using ErpApp.Application.Manufacturing.Commands.CreateProductionJournal;
using ErpApp.Application.Manufacturing.Commands.CreateProductionOrder;
using ErpApp.Application.Manufacturing.Commands.DeleteBillOfMaterials;
using ErpApp.Application.Manufacturing.Commands.UpdateBillOfMaterials;
using ErpApp.Application.Manufacturing.Commands.UpdateProductionJournal;
using ErpApp.Application.Manufacturing.Commands.UpdateProductionOrder;
using ErpApp.Application.Manufacturing.Commands.VoidProductionJournal;
using ErpApp.Application.Manufacturing.Commands.VoidProductionOrder;
using ErpApp.Application.Manufacturing.Queries.GetBillOfMaterials;
using ErpApp.Application.Manufacturing.Queries.GetBomTemplate;
using ErpApp.Application.Manufacturing.Queries.GetProductionJournal;
using ErpApp.Application.Manufacturing.Queries.GetProductionJournalConversionTemplate;
using ErpApp.Application.Manufacturing.Queries.GetProductionOrder;
using ErpApp.Application.Manufacturing.Queries.ListBillsOfMaterials;
using ErpApp.Application.Manufacturing.Queries.ListProductionJournals;
using ErpApp.Application.Manufacturing.Queries.ListProductionOrders;
using ErpApp.Application.Manufacturing.Queries.ProductionPlanning;
using ErpApp.Application.Manufacturing.Queries.ProductionSummary;
using ErpApp.Application.Manufacturing.Queries.ProductionVariance;
using ErpApp.Domain.Common;
using ErpApp.Domain.Manufacturing;
using MediatR;

namespace ErpApp.Api.Endpoints;

public static class ManufacturingEndpoints
{
    public static void MapManufacturingEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/organizations/{organizationId:guid}")
            .WithTags("Manufacturing")
            .RequireAuthorization();

        MapBillOfMaterialsEndpoints(group);
        MapProductionOrderEndpoints(group);
        MapProductionJournalEndpoints(group);
        MapReportEndpoints(group);
    }

    private sealed record BillOfMaterialsRequest(
        Guid ProductId,
        decimal OutputQuantity,
        bool ManufactureOnEverySale,
        string? Notes,
        bool IsActive,
        IReadOnlyList<ProductionRawMaterialLineInput> RawMaterials,
        IReadOnlyList<ProductionByProductLineInput> ByProducts,
        IReadOnlyList<ProductionExpenseLineInput> Expenses);

    private static void MapBillOfMaterialsEndpoints(RouteGroupBuilder group)
    {
        group.MapGet("/bills-of-materials", async (
            Guid organizationId, string? search, bool? isActive, int? page, int? pageSize,
            ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(
                new ListBillsOfMaterialsQuery(
                    organizationId, search, isActive, page ?? 1, pageSize ?? PagingDefaults.DefaultPageSize), ct);
            return Results.Ok(result);
        });

        group.MapGet("/bills-of-materials/{id:guid}", async (
            Guid organizationId, Guid id, ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(new GetBillOfMaterialsQuery(organizationId, id), ct);
            return Results.Ok(result);
        });

        group.MapPost("/bills-of-materials", async (
            Guid organizationId, BillOfMaterialsRequest request, ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(
                new CreateBillOfMaterialsCommand(
                    organizationId, request.ProductId, request.OutputQuantity, request.ManufactureOnEverySale,
                    request.Notes, request.RawMaterials, request.ByProducts, request.Expenses), ct);
            return Results.Created($"/api/organizations/{organizationId}/bills-of-materials/{result.Id}", result);
        });

        group.MapPut("/bills-of-materials/{id:guid}", async (
            Guid organizationId, Guid id, BillOfMaterialsRequest request, ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(
                new UpdateBillOfMaterialsCommand(
                    organizationId, id, request.ProductId, request.OutputQuantity, request.ManufactureOnEverySale,
                    request.Notes, request.IsActive, request.RawMaterials, request.ByProducts, request.Expenses), ct);
            return Results.Ok(result);
        });

        group.MapDelete("/bills-of-materials/{id:guid}", async (
            Guid organizationId, Guid id, ISender sender, CancellationToken ct) =>
        {
            await sender.Send(new DeleteBillOfMaterialsCommand(organizationId, id), ct);
            return Results.NoContent();
        });

        // The server side of "LOAD BOM" -- see GetBomTemplateQuery. Returns 204 when the product
        // has no recipe, which is an ordinary answer rather than a 404.
        group.MapGet("/bom-template", async (
            Guid organizationId, Guid productId, decimal outputQuantity, ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(new GetBomTemplateQuery(organizationId, productId, outputQuantity), ct);
            return result is null ? Results.NoContent() : Results.Ok(result);
        });
    }

    private sealed record ProductionOrderRequest(
        DateOnly Date,
        string? Reference,
        Guid ProductId,
        decimal OutputQuantity,
        Guid? BillOfMaterialsId,
        string? Notes,
        IReadOnlyList<ProductionRawMaterialLineInput> RawMaterials,
        IReadOnlyList<ProductionByProductLineInput> ByProducts,
        IReadOnlyList<ProductionExpenseLineInput> Expenses);

    private static void MapProductionOrderEndpoints(RouteGroupBuilder group)
    {
        group.MapGet("/production-orders", async (
            Guid organizationId, ProductionOrderStatus? status, int? page, int? pageSize,
            ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(
                new ListProductionOrdersQuery(
                    organizationId, status, page ?? 1, pageSize ?? PagingDefaults.DefaultPageSize), ct);
            return Results.Ok(result);
        });

        group.MapGet("/production-orders/{id:guid}", async (
            Guid organizationId, Guid id, ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(new GetProductionOrderQuery(organizationId, id), ct);
            return Results.Ok(result);
        });

        group.MapPost("/production-orders", async (
            Guid organizationId, ProductionOrderRequest request, ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(
                new CreateProductionOrderCommand(
                    organizationId, request.Date, request.Reference, request.ProductId, request.OutputQuantity,
                    request.BillOfMaterialsId, request.Notes, request.RawMaterials, request.ByProducts, request.Expenses),
                ct);
            return Results.Created($"/api/organizations/{organizationId}/production-orders/{result.Id}", result);
        });

        group.MapPut("/production-orders/{id:guid}", async (
            Guid organizationId, Guid id, ProductionOrderRequest request, ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(
                new UpdateProductionOrderCommand(
                    organizationId, id, request.Date, request.Reference, request.ProductId, request.OutputQuantity,
                    request.BillOfMaterialsId, request.Notes, request.RawMaterials, request.ByProducts, request.Expenses),
                ct);
            return Results.Ok(result);
        });

        group.MapPost("/production-orders/{id:guid}/approve", async (
            Guid organizationId, Guid id, ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(new ApproveProductionOrderCommand(organizationId, id), ct);
            return Results.Ok(result);
        });

        group.MapPost("/production-orders/{id:guid}/void", async (
            Guid organizationId, Guid id, ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(new VoidProductionOrderCommand(organizationId, id), ct);
            return Results.Ok(result);
        });

        group.MapGet("/production-orders/{id:guid}/production-journal-template", async (
            Guid organizationId, Guid id, ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(new GetProductionJournalConversionTemplateQuery(organizationId, id), ct);
            return Results.Ok(result);
        });
    }

    private sealed record ProductionJournalRequest(
        DateOnly Date,
        string? Reference,
        Guid ProductId,
        decimal OutputQuantity,
        Guid WarehouseId,
        Guid? BillOfMaterialsId,
        string? Notes,
        DocumentType? ReferrerType,
        Guid? ReferrerId,
        IReadOnlyList<ProductionRawMaterialLineInput> RawMaterials,
        IReadOnlyList<ProductionByProductLineInput> ByProducts,
        IReadOnlyList<ProductionExpenseLineInput> Expenses);

    private static void MapProductionJournalEndpoints(RouteGroupBuilder group)
    {
        group.MapGet("/production-journals", async (
            Guid organizationId, ProductionJournalStatus? status, int? page, int? pageSize,
            ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(
                new ListProductionJournalsQuery(
                    organizationId, status, page ?? 1, pageSize ?? PagingDefaults.DefaultPageSize), ct);
            return Results.Ok(result);
        });

        group.MapGet("/production-journals/{id:guid}", async (
            Guid organizationId, Guid id, ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(new GetProductionJournalQuery(organizationId, id), ct);
            return Results.Ok(result);
        });

        group.MapPost("/production-journals", async (
            Guid organizationId, ProductionJournalRequest request, ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(
                new CreateProductionJournalCommand(
                    organizationId, request.Date, request.Reference, request.ProductId, request.OutputQuantity,
                    request.WarehouseId, request.BillOfMaterialsId, request.Notes, request.ReferrerType,
                    request.ReferrerId, request.RawMaterials, request.ByProducts, request.Expenses),
                ct);
            return Results.Created($"/api/organizations/{organizationId}/production-journals/{result.Id}", result);
        });

        group.MapPut("/production-journals/{id:guid}", async (
            Guid organizationId, Guid id, ProductionJournalRequest request, ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(
                new UpdateProductionJournalCommand(
                    organizationId, id, request.Date, request.Reference, request.ProductId, request.OutputQuantity,
                    request.WarehouseId, request.BillOfMaterialsId, request.Notes, request.RawMaterials,
                    request.ByProducts, request.Expenses),
                ct);
            return Results.Ok(result);
        });

        group.MapPost("/production-journals/{id:guid}/approve", async (
            Guid organizationId, Guid id, bool? overrideWarning, ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(
                new ApproveProductionJournalCommand(organizationId, id, overrideWarning ?? false), ct);
            return Results.Ok(result);
        });

        group.MapPost("/production-journals/{id:guid}/void", async (
            Guid organizationId, Guid id, ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(new VoidProductionJournalCommand(organizationId, id), ct);
            return Results.Ok(result);
        });
    }

    private static void MapReportEndpoints(RouteGroupBuilder group)
    {
        group.MapGet("/reports/production-summary", async (
            Guid organizationId, DateOnly fromDate, DateOnly toDate, Guid? productId, Guid? categoryId,
            bool? exportAll, int? page, int? pageSize, ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(
                new ProductionSummaryQuery(
                    organizationId, fromDate, toDate, productId, categoryId, exportAll ?? false,
                    page ?? 1, pageSize ?? PagingDefaults.DefaultPageSize), ct);
            return Results.Ok(result);
        });

        group.MapGet("/reports/production-variance", async (
            Guid organizationId, DateOnly fromDate, DateOnly toDate, Guid? productId, Guid? categoryId,
            bool? exportAll, int? page, int? pageSize, ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(
                new ProductionVarianceQuery(
                    organizationId, fromDate, toDate, productId, categoryId, exportAll ?? false,
                    page ?? 1, pageSize ?? PagingDefaults.DefaultPageSize), ct);
            return Results.Ok(result);
        });

        group.MapGet("/reports/production-planning", async (
            Guid organizationId, Guid productId, decimal quantity, Guid? warehouseId,
            ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(
                new ProductionPlanningQuery(organizationId, productId, quantity, warehouseId), ct);
            return Results.Ok(result);
        });

        // Phase 26c closes phase 25's carried gap: these three reports shipped with no .xlsx export
        // at all, the only reports in the catalogue that could not leave the screen.
        group.MapGet("/reports/production-summary/export", async (
            Guid organizationId, DateOnly fromDate, DateOnly toDate, Guid? productId, Guid? categoryId,
            bool full, int? page, int? pageSize, ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(
                new ProductionSummaryQuery(
                    organizationId, fromDate, toDate, productId, categoryId, full,
                    page ?? 1, pageSize ?? PagingDefaults.DefaultPageSize), ct);
            return ReportSpreadsheetExporter.ExportProductionSummary(result, fromDate, toDate);
        });

        group.MapGet("/reports/production-variance/export", async (
            Guid organizationId, DateOnly fromDate, DateOnly toDate, Guid? productId, Guid? categoryId,
            bool full, int? page, int? pageSize, ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(
                new ProductionVarianceQuery(
                    organizationId, fromDate, toDate, productId, categoryId, full,
                    page ?? 1, pageSize ?? PagingDefaults.DefaultPageSize), ct);
            return ReportSpreadsheetExporter.ExportProductionVariance(result, fromDate, toDate);
        });

        // No `full`: a planning report is one product's explosion and was never paginated.
        group.MapGet("/reports/production-planning/export", async (
            Guid organizationId, Guid productId, decimal quantity, Guid? warehouseId,
            ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(
                new ProductionPlanningQuery(organizationId, productId, quantity, warehouseId), ct);
            return ReportSpreadsheetExporter.ExportProductionPlanning(result);
        });
    }
}
