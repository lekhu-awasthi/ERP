using ErpApp.Application.Common.Pagination;
using ErpApp.Application.Common.Security;
using MediatR;

namespace ErpApp.Application.Inventory.Queries.StockAgeing;

/// <summary>
/// Phase 19 decision #4 -- same 1-30/31-60/61-90/91+ day buckets as
/// ContactAgeingSummaryQueryHandler (Phase 9), live-confirmed against the real "Inventory Ageing
/// Summary Report" screen. Age = AsOfDate - StockLedgerEntry.TransactionDate, weighted by
/// QuantityRemaining; entries with TransactionDate &gt; AsOfDate are excluded (a future-dated layer
/// isn't "on hand" as of an earlier as-of date) -- when AsOfDate is today, that's every existing
/// entry, so bucket totals reconcile exactly against ProductStockPositionQuery's Balance for the
/// same product/warehouse (exit criterion #5). Rate/Amount are the overall weighted-average
/// valuation (one pair per product, not per bucket) matching the live screen.
/// </summary>
public sealed record StockAgeingQuery(
    Guid OrganizationId,
    DateOnly AsOfDate,
    Guid? ProductCategoryId,
    Guid? ProductId,
    Guid? WarehouseId,
    int Page = 1,
    int PageSize = PagingDefaults.DefaultPageSize,
    bool ExportAll = false)
    : IRequest<StockAgeingDto>, IRequirePermission, IOrganizationScoped
{
    public string PermissionKey => PermissionKeys.StockAgeingView;
}

public sealed record StockAgeingRowDto(
    Guid ProductId,
    string ProductCode,
    string ProductName,
    string CategoryName,
    string UnitShortName,
    decimal Days1To30,
    decimal Days31To60,
    decimal Days61To90,
    decimal Days91Plus,
    decimal Total,
    decimal Rate,
    decimal Amount);

public sealed record StockAgeingDto(
    DateOnly AsOfDate,
    IReadOnlyList<StockAgeingRowDto> Items,
    int Page,
    int PageSize,
    int TotalCount,
    decimal TotalDays1To30,
    decimal TotalDays31To60,
    decimal TotalDays61To90,
    decimal TotalDays91Plus,
    decimal TotalAmount);
