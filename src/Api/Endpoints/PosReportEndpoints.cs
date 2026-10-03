using ErpApp.Api.Reports;
using ErpApp.Application.Common.Pagination;
using ErpApp.Application.Pos.Queries.GetPosDashboard;
using ErpApp.Application.Pos.Queries.GetPosDayReport;
using ErpApp.Application.Pos.Queries.ListPosSessions;
using ErpApp.Application.Pos.Queries.PosOrderReport;
using ErpApp.Application.Pos.Queries.PosPaymentSummary;
using ErpApp.Domain.Pos;
using MediatR;

namespace ErpApp.Api.Endpoints;

/// <summary>
/// Phase 66 -- the POS reports and the dashboard. The ones on a POS key live under <c>/pos</c> beside the
/// till (the Day Report, the dashboard, the sessions list, the Order Report); the one on a
/// <c>Reports.</c> key under <c>/reports</c> beside every other report (the Payment Summary). Product and
/// Customer Sales, Sales Master, Sales Summary and POS activity are the existing ERP reports with a
/// <c>channel</c> parameter.
/// </summary>
public static class PosReportEndpoints
{
    public static void MapPosReportEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/organizations/{organizationId:guid}")
            .WithTags("Point of sale reports")
            .RequireAuthorization();

        group.MapGet("/pos/dashboard", async (
            Guid organizationId, DateOnly fromDate, DateOnly toDate, Guid? locationId, ISender sender, CancellationToken ct) =>
            Results.Ok(await sender.Send(new GetPosDashboardQuery(organizationId, fromDate, toDate, locationId), ct)));

        group.MapGet("/pos/reports/day", async (
            Guid organizationId, DateOnly fromDate, DateOnly toDate, Guid? locationId, ISender sender, CancellationToken ct) =>
            Results.Ok(await sender.Send(new GetPosDayReportQuery(organizationId, fromDate, toDate, locationId), ct)));

        group.MapGet("/pos/reports/day/export", async (
            Guid organizationId, DateOnly fromDate, DateOnly toDate, Guid? locationId, ISender sender, CancellationToken ct) =>
            ReportSpreadsheetExporter.ExportPosDayReport(
                await sender.Send(new GetPosDayReportQuery(organizationId, fromDate, toDate, locationId), ct)));

        group.MapGet("/pos/sessions", async (
            Guid organizationId, DateOnly fromDate, DateOnly toDate, Guid? locationId, PosSessionStatus? status,
            int? page, int? pageSize, string? search, ISender sender, CancellationToken ct) =>
            Results.Ok(await sender.Send(
                new ListPosSessionsQuery(
                    organizationId, fromDate, toDate, locationId, status, page ?? 1, pageSize ?? PagingDefaults.DefaultPageSize,
                    Search: search),
                ct)));

        group.MapGet("/pos/sessions/export", async (
            Guid organizationId, DateOnly fromDate, DateOnly toDate, Guid? locationId, PosSessionStatus? status,
            string? search, ISender sender, CancellationToken ct) =>
            ReportSpreadsheetExporter.ExportPosSessions(
                await sender.Send(
                    new ListPosSessionsQuery(
                        organizationId, fromDate, toDate, locationId, status, ExportAll: true, Search: search), ct),
                fromDate,
                toDate));

        group.MapGet("/pos/reports/orders", async (
            Guid organizationId, DateOnly fromDate, DateOnly toDate, Guid? locationId, PosOrderStatus? status,
            PosTab? orderType, int? page, int? pageSize, ISender sender, CancellationToken ct) =>
            Results.Ok(await sender.Send(
                new PosOrderReportQuery(
                    organizationId, fromDate, toDate, locationId, status, orderType,
                    page ?? 1, pageSize ?? PagingDefaults.DefaultPageSize),
                ct)));

        group.MapGet("/pos/reports/orders/export", async (
            Guid organizationId, DateOnly fromDate, DateOnly toDate, Guid? locationId, PosOrderStatus? status,
            PosTab? orderType, bool full, int? page, int? pageSize, ISender sender, CancellationToken ct) =>
            ReportSpreadsheetExporter.ExportPosOrderReport(
                await sender.Send(
                    new PosOrderReportQuery(
                        organizationId, fromDate, toDate, locationId, status, orderType,
                        page ?? 1, pageSize ?? PagingDefaults.DefaultPageSize, ExportAll: full),
                    ct)));

        group.MapGet("/reports/pos-payment-summary", async (
            Guid organizationId, DateOnly fromDate, DateOnly toDate, Guid? locationId, PosPaymentType? type,
            Guid? paymentModeId, int? page, int? pageSize, ISender sender, CancellationToken ct) =>
            Results.Ok(await sender.Send(
                new PosPaymentSummaryQuery(
                    organizationId, fromDate, toDate, locationId, type, paymentModeId,
                    page ?? 1, pageSize ?? PagingDefaults.DefaultPageSize),
                ct)));

        group.MapGet("/reports/pos-payment-summary/export", async (
            Guid organizationId, DateOnly fromDate, DateOnly toDate, Guid? locationId, PosPaymentType? type,
            Guid? paymentModeId, bool full, int? page, int? pageSize, ISender sender, CancellationToken ct) =>
            ReportSpreadsheetExporter.ExportPosPaymentSummary(
                await sender.Send(
                    new PosPaymentSummaryQuery(
                        organizationId, fromDate, toDate, locationId, type, paymentModeId,
                        page ?? 1, pageSize ?? PagingDefaults.DefaultPageSize, ExportAll: full),
                    ct)));
    }
}
