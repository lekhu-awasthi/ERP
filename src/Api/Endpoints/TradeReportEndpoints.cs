using ErpApp.Api.Reports;
using ErpApp.Application.Common.Pagination;
using ErpApp.Application.Trade;
using ErpApp.Application.Trade.Queries.SalesSummaryReport;
using ErpApp.Application.Trade.Queries.TradeByContact;
using ErpApp.Application.Trade.Queries.TradeByContactMonthly;
using ErpApp.Application.Trade.Queries.TradeByItem;
using ErpApp.Application.Trade.Queries.TradeByItemMonthly;
using MediatR;

namespace ErpApp.Api.Endpoints;

/// <summary>
/// Phase 26b's Sales Report and Purchase Report catalogue groups. These live in their own endpoint
/// file rather than being split across SalesEndpoints and PurchasingEndpoints because each pair is
/// answered by <b>one</b> handler discriminated by <c>TradeSide</c> -- splitting the routes would
/// mean two files importing the same query type to send it opposite constants, which reads as two
/// features rather than one mirrored pair.
///
/// <para>The side is hardcoded at the route, never bound from the query string: that is what makes
/// the two permission keys real, since <c>AuthorizationBehavior</c> reads
/// <c>PermissionKey</c> off the request the route constructed. It is the same choice
/// <c>ContactsEndpoints.MapReportEndpoints</c> makes for Customer-versus-Supplier and
/// <c>CreatePaymentCommand</c> makes for Direction.</para>
/// </summary>
public static class TradeReportEndpoints
{
    public static void MapTradeReportEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/organizations/{organizationId:guid}")
            .WithTags("Trade Reports")
            .RequireAuthorization();

        group.MapGet("/reports/sales-by-customer", async (
            Guid organizationId, DateOnly fromDate, DateOnly toDate, Guid? contactGroupId,
            int? page, int? pageSize, ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(
                new TradeByContactQuery(
                    organizationId, TradeSide.Sales, fromDate, toDate, contactGroupId,
                    page ?? 1, pageSize ?? PagingDefaults.DefaultPageSize),
                ct);
            return Results.Ok(result);
        });

        group.MapGet("/reports/sales-by-customer/export", async (
            Guid organizationId, DateOnly fromDate, DateOnly toDate, Guid? contactGroupId,
            bool full, int? page, int? pageSize, ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(
                new TradeByContactQuery(
                    organizationId, TradeSide.Sales, fromDate, toDate, contactGroupId,
                    page ?? 1, pageSize ?? PagingDefaults.DefaultPageSize, ExportAll: full),
                ct);
            return ReportSpreadsheetExporter.ExportTradeByContact(result, "Customer", "Sales By Customer");
        });

        group.MapGet("/reports/purchase-by-supplier", async (
            Guid organizationId, DateOnly fromDate, DateOnly toDate, Guid? contactGroupId,
            int? page, int? pageSize, ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(
                new TradeByContactQuery(
                    organizationId, TradeSide.Purchase, fromDate, toDate, contactGroupId,
                    page ?? 1, pageSize ?? PagingDefaults.DefaultPageSize),
                ct);
            return Results.Ok(result);
        });

        group.MapGet("/reports/purchase-by-supplier/export", async (
            Guid organizationId, DateOnly fromDate, DateOnly toDate, Guid? contactGroupId,
            bool full, int? page, int? pageSize, ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(
                new TradeByContactQuery(
                    organizationId, TradeSide.Purchase, fromDate, toDate, contactGroupId,
                    page ?? 1, pageSize ?? PagingDefaults.DefaultPageSize, ExportAll: full),
                ct);
            return ReportSpreadsheetExporter.ExportTradeByContact(result, "Supplier", "Purchase By Supplier");
        });

        group.MapGet("/reports/sales-by-item", async (
            Guid organizationId, DateOnly fromDate, DateOnly toDate, TradeItemGrouping? groupBy,
            Guid? productCategoryId, Guid? productId, int? page, int? pageSize,
            ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(
                new TradeByItemQuery(
                    organizationId, TradeSide.Sales, fromDate, toDate, groupBy ?? TradeItemGrouping.Item,
                    productCategoryId, productId, page ?? 1, pageSize ?? PagingDefaults.DefaultPageSize),
                ct);
            return Results.Ok(result);
        });

        group.MapGet("/reports/sales-by-item/export", async (
            Guid organizationId, DateOnly fromDate, DateOnly toDate, TradeItemGrouping? groupBy,
            Guid? productCategoryId, Guid? productId, bool full, int? page, int? pageSize,
            ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(
                new TradeByItemQuery(
                    organizationId, TradeSide.Sales, fromDate, toDate, groupBy ?? TradeItemGrouping.Item,
                    productCategoryId, productId, page ?? 1, pageSize ?? PagingDefaults.DefaultPageSize,
                    ExportAll: full),
                ct);
            return ReportSpreadsheetExporter.ExportTradeByItem(result, "Sales By Item");
        });

        group.MapGet("/reports/purchase-by-item", async (
            Guid organizationId, DateOnly fromDate, DateOnly toDate, TradeItemGrouping? groupBy,
            Guid? productCategoryId, Guid? productId, int? page, int? pageSize,
            ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(
                new TradeByItemQuery(
                    organizationId, TradeSide.Purchase, fromDate, toDate, groupBy ?? TradeItemGrouping.Item,
                    productCategoryId, productId, page ?? 1, pageSize ?? PagingDefaults.DefaultPageSize),
                ct);
            return Results.Ok(result);
        });

        group.MapGet("/reports/purchase-by-item/export", async (
            Guid organizationId, DateOnly fromDate, DateOnly toDate, TradeItemGrouping? groupBy,
            Guid? productCategoryId, Guid? productId, bool full, int? page, int? pageSize,
            ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(
                new TradeByItemQuery(
                    organizationId, TradeSide.Purchase, fromDate, toDate, groupBy ?? TradeItemGrouping.Item,
                    productCategoryId, productId, page ?? 1, pageSize ?? PagingDefaults.DefaultPageSize,
                    ExportAll: full),
                ct);
            return ReportSpreadsheetExporter.ExportTradeByItem(result, "Purchase By Item");
        });

        group.MapGet("/reports/sales-by-customer-monthly", async (
            Guid organizationId, int fiscalYear, Guid? contactGroupId, int? page, int? pageSize,
            ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(
                new TradeByContactMonthlyQuery(
                    organizationId, TradeSide.Sales, fiscalYear, contactGroupId,
                    page ?? 1, pageSize ?? PagingDefaults.DefaultPageSize),
                ct);
            return Results.Ok(result);
        });

        group.MapGet("/reports/sales-by-customer-monthly/export", async (
            Guid organizationId, int fiscalYear, Guid? contactGroupId, bool full, int? page, int? pageSize,
            ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(
                new TradeByContactMonthlyQuery(
                    organizationId, TradeSide.Sales, fiscalYear, contactGroupId,
                    page ?? 1, pageSize ?? PagingDefaults.DefaultPageSize, ExportAll: full),
                ct);
            return ReportSpreadsheetExporter.ExportTradeByContactMonthly(result, "Customer", "Sales By Customer Monthly");
        });

        group.MapGet("/reports/purchase-by-supplier-monthly", async (
            Guid organizationId, int fiscalYear, Guid? contactGroupId, int? page, int? pageSize,
            ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(
                new TradeByContactMonthlyQuery(
                    organizationId, TradeSide.Purchase, fiscalYear, contactGroupId,
                    page ?? 1, pageSize ?? PagingDefaults.DefaultPageSize),
                ct);
            return Results.Ok(result);
        });

        group.MapGet("/reports/purchase-by-supplier-monthly/export", async (
            Guid organizationId, int fiscalYear, Guid? contactGroupId, bool full, int? page, int? pageSize,
            ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(
                new TradeByContactMonthlyQuery(
                    organizationId, TradeSide.Purchase, fiscalYear, contactGroupId,
                    page ?? 1, pageSize ?? PagingDefaults.DefaultPageSize, ExportAll: full),
                ct);
            return ReportSpreadsheetExporter.ExportTradeByContactMonthly(result, "Supplier", "Purchase By Supplier Monthly");
        });

        group.MapGet("/reports/sales-by-item-monthly", async (
            Guid organizationId, int fiscalYear, int? page, int? pageSize,
            ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(
                new TradeByItemMonthlyQuery(
                    organizationId, TradeSide.Sales, fiscalYear,
                    page ?? 1, pageSize ?? PagingDefaults.DefaultPageSize),
                ct);
            return Results.Ok(result);
        });

        group.MapGet("/reports/sales-by-item-monthly/export", async (
            Guid organizationId, int fiscalYear, bool full, int? page, int? pageSize,
            ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(
                new TradeByItemMonthlyQuery(
                    organizationId, TradeSide.Sales, fiscalYear,
                    page ?? 1, pageSize ?? PagingDefaults.DefaultPageSize, ExportAll: full),
                ct);
            return ReportSpreadsheetExporter.ExportTradeByItemMonthly(result, "Sales By Item Monthly");
        });

        group.MapGet("/reports/purchase-by-item-monthly", async (
            Guid organizationId, int fiscalYear, int? page, int? pageSize,
            ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(
                new TradeByItemMonthlyQuery(
                    organizationId, TradeSide.Purchase, fiscalYear,
                    page ?? 1, pageSize ?? PagingDefaults.DefaultPageSize),
                ct);
            return Results.Ok(result);
        });

        group.MapGet("/reports/purchase-by-item-monthly/export", async (
            Guid organizationId, int fiscalYear, bool full, int? page, int? pageSize,
            ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(
                new TradeByItemMonthlyQuery(
                    organizationId, TradeSide.Purchase, fiscalYear,
                    page ?? 1, pageSize ?? PagingDefaults.DefaultPageSize, ExportAll: full),
                ct);
            return ReportSpreadsheetExporter.ExportTradeByItemMonthly(result, "Purchase By Item Monthly");
        });

        // The one report in this group with no mirror -- there is no Purchase Summary Report in the
        // live catalogue.
        group.MapGet("/reports/sales-summary", async (
            Guid organizationId, int fiscalYear, SalesSummaryMode? mode, int? page, int? pageSize,
            ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(
                new SalesSummaryReportQuery(
                    organizationId, fiscalYear, mode ?? SalesSummaryMode.Month,
                    page ?? 1, pageSize ?? PagingDefaults.DefaultPageSize),
                ct);
            return Results.Ok(result);
        });

        group.MapGet("/reports/sales-summary/export", async (
            Guid organizationId, int fiscalYear, SalesSummaryMode? mode, bool full, int? page, int? pageSize,
            ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(
                new SalesSummaryReportQuery(
                    organizationId, fiscalYear, mode ?? SalesSummaryMode.Month,
                    page ?? 1, pageSize ?? PagingDefaults.DefaultPageSize, ExportAll: full),
                ct);
            return ReportSpreadsheetExporter.ExportSalesSummaryReport(result);
        });
    }
}
