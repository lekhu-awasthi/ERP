using ErpApp.Application.Common.Pagination;
using ErpApp.Application.Common.Security;
using MediatR;

namespace ErpApp.Application.Inventory.Queries.ProductProfitability;

/// <summary>
/// Phase 19 decision #5 -- a per-product-per-period aggregate (live-confirmed: one row per Product,
/// not one per transaction line like Sales Master Report). Sales = sum(InvoiceLine.Amount);
/// CostOfSales = sum(InvoiceLine.CogsUnitCost * Quantity) -- both already stored at Invoice-Approve
/// time (Phase 7), no new write-side work. ProductionCost/Consumption/AdditionalCost are always 0
/// (Manufacturing and Cost Terms/landed-cost are unbuilt -- see phase-19-status.md's known
/// limitations) but ship in the DTO shape rather than being silently dropped, matching the live
/// screen's column set. Opening/ClosingBalance reuse ProductStockPositionQuery's own precedent of
/// not modeling a true historical point-in-time balance -- they value StockLedgerEntry's *current*
/// QuantityRemaining filtered by TransactionDate, an approximation, not a day-zero snapshot.
/// </summary>
public sealed record ProductProfitabilityQuery(
    Guid OrganizationId,
    DateOnly FromDate,
    DateOnly ToDate,
    Guid? ProductCategoryId,
    Guid? ProductId,
    int Page = 1,
    int PageSize = PagingDefaults.DefaultPageSize,
    bool ExportAll = false)
    : IRequest<ProductProfitabilityDto>, IRequirePermission, IOrganizationScoped
{
    public string PermissionKey => PermissionKeys.ProductProfitabilityView;
}

public sealed record ProductProfitabilityRowDto(
    Guid ProductId,
    string ProductCode,
    string ProductName,
    string CategoryName,
    decimal OpeningBalance,
    decimal Purchase,
    decimal ProductionCost,
    decimal AdditionalCost,
    decimal ClosingBalance,
    decimal CostOfSales,
    decimal Sales,
    decimal Consumption,
    decimal GrossProfit,
    decimal GrossMarginPct);

public sealed record ProductProfitabilityDto(
    DateOnly FromDate,
    DateOnly ToDate,
    IReadOnlyList<ProductProfitabilityRowDto> Items,
    int Page,
    int PageSize,
    int TotalCount,
    decimal TotalSales,
    decimal TotalCostOfSales,
    decimal TotalGrossProfit);
