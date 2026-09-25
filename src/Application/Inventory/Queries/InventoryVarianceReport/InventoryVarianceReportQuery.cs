using ErpApp.Application.Common.Locations;
using ErpApp.Application.Common.Pagination;
using ErpApp.Application.Common.Security;
using ErpApp.Domain.Tenancy;
using MediatR;

namespace ErpApp.Application.Inventory.Queries.InventoryVarianceReport;

/// <summary>
/// Phase 58 -- the <b>Inventory Variance Report</b> (<c>report-inventory-variance</c>): where the
/// books and the shelf disagree. Read live on 2026-09-24: seven columns, Item code / Item Name /
/// Item Category / Book Balance / Actual Balance / Difference / Remarks, with <i>Book</i> the
/// accounting ledger and <i>Actual</i> the physical one.
///
/// <para><b>Both balances come from <c>StockFactReader</c></b>, the reader Inventory Position uses,
/// so this report's Book Balance is Inventory Position's quantity in Accounting mode and its Actual
/// Balance is the same report's quantity in Physical mode, product for product, by construction.</para>
///
/// <para><b>Three translations, each recorded.</b> (1) The live date control is a range, but both
/// columns are <i>balances</i>, so only its end can mean anything; the query takes that end as
/// <see cref="AsOfDate"/> and the screen labels it "As of" rather than displaying a start it would not
/// apply (phase 34b). (2) The live "Group by Item/Category" control holds "All" and narrows rows; a
/// quantity cannot be summed across products of different units, so it is a Category and a Product
/// filter here, not a grouping. (3) Only products whose two balances differ are listed -- a variance
/// report of agreeing rows is noise, and the one-product live tenant could not show which the vendor
/// does.</para>
///
/// <para>Gated like every stock report on <see cref="TenantFeature.TrackInventory"/>, and <b>not</b>
/// on the tenant's Mode of Inventory Tracking: the live product refuses it in Accounting Movement,
/// but here the physical ledger's shared half exists in either mode, and a report that a setting can
/// silently empty is better shown than hidden. The screen says which mode the tenant runs.</para>
/// </summary>
public sealed record InventoryVarianceReportQuery(
    Guid OrganizationId,
    DateOnly AsOfDate,
    Guid? CategoryId = null,
    Guid? ProductId = null,
    int Page = 1,
    int PageSize = PagingDefaults.DefaultPageSize,
    bool ExportAll = false,
    Guid? LocationId = null)
    : IRequest<InventoryVarianceReportDto>, IRequirePermission, IOrganizationScoped, IRequireFeature, ILocationFilteredReport
{
    public string PermissionKey => PermissionKeys.InventoryVarianceView;

    public IReadOnlyCollection<TenantFeature> RequiredFeatures => [TenantFeature.TrackInventory];
}

/// <summary>Which side of the books the shelf is on -- the live Remarks column's two phrasings,
/// verbatim, keyed by the sign of Actual minus Book.</summary>
public enum InventoryVarianceDirection
{
    /// <summary>Actual above Book: "Quantity To Be Shipped".</summary>
    ToBeShipped,

    /// <summary>Actual below Book: "Quantity To Be Received".</summary>
    ToBeReceived,
}

/// <param name="Difference">The absolute gap, as the live column prints it (Book 3, Actual -3 reads
/// "6"); <paramref name="Direction"/> carries the sign.</param>
public sealed record InventoryVarianceRowDto(
    Guid ProductId,
    string Code,
    string Name,
    string Category,
    string Unit,
    decimal BookBalance,
    decimal ActualBalance,
    decimal Difference,
    InventoryVarianceDirection Direction);

/// <param name="Mode">The tenant's Mode of Inventory Tracking, so the screen can say whether the
/// physical ledger is the one this tenant runs on.</param>
public sealed record InventoryVarianceReportDto(
    DateOnly AsOfDate,
    IReadOnlyList<InventoryVarianceRowDto> Items,
    int Page,
    int PageSize,
    int TotalCount,
    InventoryTrackingMode Mode);
