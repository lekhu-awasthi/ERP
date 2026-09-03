using ErpApp.Application.Common.Pagination;
using ErpApp.Application.Common.Security;
using ErpApp.Domain.Tenancy;
using MediatR;

namespace ErpApp.Application.Inventory.Queries.InventoryPositionReport;

/// <summary>
/// The Inventory Report group's <b>Inventory Position</b> (phase 26c, slug
/// <c>inventory-summary</c>). Read live on 2026-09-03: filters Period, Product Category, Product
/// (drawer adds Warehouse-grouping and a positive/negative balance switch); columns Code/Goods,
/// Category, Qty, UOM, Rate, Amount; one row per product; footer Total over Qty and Amount only.
///
/// <para><b>This is Inventory Movement's Balance columns, and nothing else.</b> Both handlers read
/// <see cref="Reports.StockFactReader"/>, so the two reports cannot disagree -- which is what the
/// live pair does (Movement's Balance triple and Position's Qty/Rate/Amount matched product for
/// product). See phase-26b's <c>ContactLedgerReader</c> precedent for why that is stated as a
/// design property rather than left to luck.</para>
///
/// <para><b>Not the same thing as the existing <c>ProductStockPositionQuery</c></b> (phase 7),
/// which stays where it is. That one answers the Inventory <i>module</i>'s per-warehouse
/// quantity grid: no date range, no value, opening hardcoded to zero, and gated by
/// <c>InventoryLedgerView</c> alongside the product screens. This is a <i>report</i> -- dated,
/// valued, category-filterable, paginated, exportable and separately permissioned. Making the old
/// query grow a period and a valuation to serve both would have changed what four shipped screens
/// return; a report page is the thing being added, so a report query is what it gets.</para>
/// </summary>
public sealed record InventoryPositionReportQuery(
    Guid OrganizationId,
    DateOnly FromDate,
    DateOnly ToDate,
    Guid? CategoryId,
    Guid? ProductId,
    Guid? WarehouseId,
    InventoryBalanceFilter BalanceFilter = InventoryBalanceFilter.All,
    int Page = 1,
    int PageSize = PagingDefaults.DefaultPageSize,
    bool ExportAll = false)
    : IRequest<InventoryPositionReportDto>, IRequirePermission, IOrganizationScoped, IRequireFeature
{
    public string PermissionKey => PermissionKeys.InventoryPositionView;

    public IReadOnlyCollection<TenantFeature> RequiredFeatures => [TenantFeature.TrackInventory];
}

/// <summary>The live drawer's View Options radio: Show All / positive only / negative only.</summary>
public enum InventoryBalanceFilter
{
    All = 0,
    PositiveOnly = 1,
    NegativeOnly = 2,
}

public sealed record InventoryPositionRowDto(
    Guid ProductId,
    string Product,
    string Category,
    decimal Quantity,
    string Unit,
    decimal Rate,
    decimal Amount);

/// <summary>
/// <paramref name="TotalQuantity"/> and <paramref name="TotalAmount"/> cover the whole filtered
/// set, not the page -- phase-16c bug #1. The live footer totals exactly these two columns and
/// leaves Rate blank, which is right: rates over different units do not add.
/// </summary>
public sealed record InventoryPositionReportDto(
    DateOnly FromDate,
    DateOnly ToDate,
    IReadOnlyList<InventoryPositionRowDto> Items,
    int Page,
    int PageSize,
    int TotalCount,
    decimal TotalQuantity,
    decimal TotalAmount);
