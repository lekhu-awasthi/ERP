using ErpApp.Api.Reports;
using ErpApp.Application.Accounting.Queries.ExceptionalReport;
using ErpApp.Application.Accounting.Queries.NetTradingAssets;
using ErpApp.Application.Common.Pagination;
using ErpApp.Application.Identity.Queries.UserLog;
using ErpApp.Application.Inventory.Queries.InventoryLedgerReport;
using ErpApp.Application.Inventory.Queries.InventoryMasterReport;
using ErpApp.Application.Inventory.Queries.InventoryMovementReport;
using ErpApp.Application.Inventory.Queries.InventoryPositionReport;
using ErpApp.Application.Purchasing.Queries.PurchaseReturnRegister;
using ErpApp.Application.Sales.Queries.SalesReturnRegister;
using ErpApp.Domain.Common;
using MediatR;

namespace ErpApp.Api.Endpoints;

/// <summary>
/// Phase 26c's nine reports -- the Inventory, Tax, System and Analytics catalogue groups that
/// completed the Reports landing page.
///
/// <para>They live in one endpoint file rather than being scattered into SalesEndpoints,
/// PurchasingEndpoints, InventoryEndpoints and AccountingEndpoints for the reason
/// <c>TradeReportEndpoints</c> gives for phase 26b: these routes are one feature -- a catalogue of
/// read-only, period-filtered, exportable reports -- and splitting them across four files by which
/// aggregate they happen to read would bury that. The two return registers in particular belong
/// beside each other: their whole design story is that they are <i>not</i> mirrors.</para>
///
/// <para>Every report is paired with an <c>/export</c> route taking <c>full</c>, which sets
/// <c>ExportAll</c> so the spreadsheet carries the entire filtered set rather than the page on
/// screen -- phase-16c's rule.</para>
/// </summary>
public static class CatalogueReportEndpoints
{
    public static void MapCatalogueReportEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/organizations/{organizationId:guid}")
            .WithTags("Catalogue Reports")
            .RequireAuthorization();

        MapReturnRegisters(group);
        MapInventoryReports(group);
        MapAnalyticsReports(group);
        MapSystemReports(group);
    }

    private static void MapReturnRegisters(RouteGroupBuilder group)
    {
        group.MapGet("/reports/sales-return-register", async (
            Guid organizationId, DateOnly fromDate, DateOnly toDate, Guid? contactId,
            int? page, int? pageSize, ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(
                new SalesReturnRegisterQuery(
                    organizationId, fromDate, toDate, contactId,
                    page ?? 1, pageSize ?? PagingDefaults.DefaultPageSize),
                ct);
            return Results.Ok(result);
        });

        group.MapGet("/reports/sales-return-register/export", async (
            Guid organizationId, DateOnly fromDate, DateOnly toDate, Guid? contactId,
            bool full, int? page, int? pageSize, ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(
                new SalesReturnRegisterQuery(
                    organizationId, fromDate, toDate, contactId,
                    page ?? 1, pageSize ?? PagingDefaults.DefaultPageSize, ExportAll: full),
                ct);
            return ReportSpreadsheetExporter.ExportSalesReturnRegister(result);
        });

        group.MapGet("/reports/purchase-return-register", async (
            Guid organizationId, DateOnly fromDate, DateOnly toDate, Guid? contactId,
            int? page, int? pageSize, ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(
                new PurchaseReturnRegisterQuery(
                    organizationId, fromDate, toDate, contactId,
                    page ?? 1, pageSize ?? PagingDefaults.DefaultPageSize),
                ct);
            return Results.Ok(result);
        });

        group.MapGet("/reports/purchase-return-register/export", async (
            Guid organizationId, DateOnly fromDate, DateOnly toDate, Guid? contactId,
            bool full, int? page, int? pageSize, ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(
                new PurchaseReturnRegisterQuery(
                    organizationId, fromDate, toDate, contactId,
                    page ?? 1, pageSize ?? PagingDefaults.DefaultPageSize, ExportAll: full),
                ct);
            return ReportSpreadsheetExporter.ExportPurchaseReturnRegister(result);
        });
    }

    private static void MapInventoryReports(RouteGroupBuilder group)
    {
        group.MapGet("/reports/inventory-position", async (
            Guid organizationId, DateOnly fromDate, DateOnly toDate,
            Guid? categoryId, Guid? productId, Guid? warehouseId, InventoryBalanceFilter? balanceFilter,
            int? page, int? pageSize, ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(
                new InventoryPositionReportQuery(
                    organizationId, fromDate, toDate, categoryId, productId, warehouseId,
                    balanceFilter ?? InventoryBalanceFilter.All,
                    page ?? 1, pageSize ?? PagingDefaults.DefaultPageSize),
                ct);
            return Results.Ok(result);
        });

        group.MapGet("/reports/inventory-position/export", async (
            Guid organizationId, DateOnly fromDate, DateOnly toDate,
            Guid? categoryId, Guid? productId, Guid? warehouseId, InventoryBalanceFilter? balanceFilter,
            bool full, int? page, int? pageSize, ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(
                new InventoryPositionReportQuery(
                    organizationId, fromDate, toDate, categoryId, productId, warehouseId,
                    balanceFilter ?? InventoryBalanceFilter.All,
                    page ?? 1, pageSize ?? PagingDefaults.DefaultPageSize, ExportAll: full),
                ct);
            return ReportSpreadsheetExporter.ExportInventoryPosition(result);
        });

        group.MapGet("/reports/inventory-movement", async (
            Guid organizationId, DateOnly fromDate, DateOnly toDate,
            Guid? categoryId, Guid? productId, Guid? warehouseId,
            int? page, int? pageSize, ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(
                new InventoryMovementReportQuery(
                    organizationId, fromDate, toDate, categoryId, productId, warehouseId,
                    page ?? 1, pageSize ?? PagingDefaults.DefaultPageSize),
                ct);
            return Results.Ok(result);
        });

        group.MapGet("/reports/inventory-movement/export", async (
            Guid organizationId, DateOnly fromDate, DateOnly toDate,
            Guid? categoryId, Guid? productId, Guid? warehouseId,
            bool full, int? page, int? pageSize, ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(
                new InventoryMovementReportQuery(
                    organizationId, fromDate, toDate, categoryId, productId, warehouseId,
                    page ?? 1, pageSize ?? PagingDefaults.DefaultPageSize, ExportAll: full),
                ct);
            return ReportSpreadsheetExporter.ExportInventoryMovement(result);
        });

        group.MapGet("/reports/inventory-ledger", async (
            Guid organizationId, DateOnly fromDate, DateOnly toDate, Guid productId, Guid? warehouseId,
            int? page, int? pageSize, ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(
                new InventoryLedgerReportQuery(
                    organizationId, fromDate, toDate, productId, warehouseId,
                    page ?? 1, pageSize ?? PagingDefaults.DefaultPageSize),
                ct);
            return Results.Ok(result);
        });

        group.MapGet("/reports/inventory-ledger/export", async (
            Guid organizationId, DateOnly fromDate, DateOnly toDate, Guid productId, Guid? warehouseId,
            bool full, int? page, int? pageSize, ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(
                new InventoryLedgerReportQuery(
                    organizationId, fromDate, toDate, productId, warehouseId,
                    page ?? 1, pageSize ?? PagingDefaults.DefaultPageSize, ExportAll: full),
                ct);
            return ReportSpreadsheetExporter.ExportInventoryLedgerReport(result);
        });

        group.MapGet("/reports/inventory-master", async (
            Guid organizationId, DateOnly fromDate, DateOnly toDate,
            Guid? contactId, Guid? productId, DocumentType? documentType,
            int? page, int? pageSize, ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(
                new InventoryMasterReportQuery(
                    organizationId, fromDate, toDate, contactId, productId, documentType,
                    page ?? 1, pageSize ?? PagingDefaults.DefaultPageSize),
                ct);
            return Results.Ok(result);
        });

        group.MapGet("/reports/inventory-master/export", async (
            Guid organizationId, DateOnly fromDate, DateOnly toDate,
            Guid? contactId, Guid? productId, DocumentType? documentType,
            bool full, int? page, int? pageSize, ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(
                new InventoryMasterReportQuery(
                    organizationId, fromDate, toDate, contactId, productId, documentType,
                    page ?? 1, pageSize ?? PagingDefaults.DefaultPageSize, ExportAll: full),
                ct);
            return ReportSpreadsheetExporter.ExportInventoryMasterReport(result);
        });
    }

    private static void MapAnalyticsReports(RouteGroupBuilder group)
    {
        // No paging on either of these two: both are fixed-row reports (four rows and twelve),
        // so there is nothing to page and no ExportAll to pass.
        group.MapGet("/reports/net-trading-assets", async (
            Guid organizationId, DateOnly fromDate, DateOnly toDate, bool? compare, bool? excludeAdvance,
            ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(
                new NetTradingAssetsQuery(
                    organizationId, fromDate, toDate, compare ?? false, excludeAdvance ?? false),
                ct);
            return Results.Ok(result);
        });

        group.MapGet("/reports/net-trading-assets/export", async (
            Guid organizationId, DateOnly fromDate, DateOnly toDate, bool? compare, bool? excludeAdvance,
            ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(
                new NetTradingAssetsQuery(
                    organizationId, fromDate, toDate, compare ?? false, excludeAdvance ?? false),
                ct);
            return ReportSpreadsheetExporter.ExportNetTradingAssets(result);
        });

        group.MapGet("/reports/exceptional-report", async (
            Guid organizationId, DateOnly fromDate, DateOnly toDate, ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(new ExceptionalReportQuery(organizationId, fromDate, toDate), ct);
            return Results.Ok(result);
        });

        group.MapGet("/reports/exceptional-report/export", async (
            Guid organizationId, DateOnly fromDate, DateOnly toDate, ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(new ExceptionalReportQuery(organizationId, fromDate, toDate), ct);
            return ReportSpreadsheetExporter.ExportExceptionalReport(result);
        });
    }

    private static void MapSystemReports(RouteGroupBuilder group)
    {
        group.MapGet("/reports/user-log", async (
            Guid organizationId, DateOnly fromDate, DateOnly toDate, Guid? userId,
            int? page, int? pageSize, ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(
                new UserLogQuery(
                    organizationId, fromDate, toDate, userId,
                    page ?? 1, pageSize ?? PagingDefaults.DefaultPageSize),
                ct);
            return Results.Ok(result);
        });

        group.MapGet("/reports/user-log/export", async (
            Guid organizationId, DateOnly fromDate, DateOnly toDate, Guid? userId,
            bool full, int? page, int? pageSize, ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(
                new UserLogQuery(
                    organizationId, fromDate, toDate, userId,
                    page ?? 1, pageSize ?? PagingDefaults.DefaultPageSize, ExportAll: full),
                ct);
            return ReportSpreadsheetExporter.ExportUserLog(result);
        });
    }
}
