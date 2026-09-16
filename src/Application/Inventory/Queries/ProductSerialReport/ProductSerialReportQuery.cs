using ErpApp.Application.Common.Locations;
using ErpApp.Application.Common.Pagination;
using ErpApp.Application.Common.Security;
using ErpApp.Domain.Tenancy;
using MediatR;

namespace ErpApp.Application.Inventory.Queries.ProductSerialReport;

/// <summary>
/// The Inventory Report group's <b>Product Serial No Report</b> (phase 51, reference slug
/// <c>serial-number-tracking-report</c>).
///
/// <para><b>Its column set was never read</b> -- see <c>ProductBatchReportQuery</c> for the full
/// statement of that and why (phase-8f, invoked explicitly in phase-51-status.md Decision A). What
/// was readable: the product detail <b>Serial Number tab</b> -- columns SERIAL NO. / WAREHOUSE /
/// CREATED AT, eight serials observed, all in Kathmandu -- and this report's own catalogue filters,
/// Period / Group By / Status, with Group By offering None (default) / Product / Warehouse.</para>
///
/// <para><b>The Status filter is the interesting one, and it was not invented.</b> The kickoff
/// flagged it as a trap: a Status filter implies a serial has a lifecycle that the tab's three
/// columns do not show, and the instruction was to treat that as a question for the read rather
/// than an invention. The model answered it instead. Because a serial <i>is</i> a FIFO layer of
/// quantity one (phase-51 Decision C), its lifecycle is already in the ledger:
/// <c>QuantityRemaining</c> is <c>1</c> while the unit is in stock and <c>0</c> once it has been
/// issued. So <see cref="ProductSerialStatus"/> is a projection of a column that has existed since
/// phase 7, not a new concept -- which is the strongest evidence available that the modelling
/// decision was the right one.</para>
///
/// <para><b>Period means received-in-period</b>, on the same reasoning as the batch report: a
/// serial's row is its layer, and a layer's <c>TransactionDate</c> is its source document's
/// business date.</para>
/// </summary>
public sealed record ProductSerialReportQuery(
    Guid OrganizationId,
    DateOnly FromDate,
    DateOnly ToDate,
    Guid? ProductId,
    Guid? WarehouseId,
    ProductSerialStatusFilter Status = ProductSerialStatusFilter.All,
    ProductSerialGroupBy GroupBy = ProductSerialGroupBy.None,
    int Page = 1,
    int PageSize = PagingDefaults.DefaultPageSize,
    bool ExportAll = false,
    Guid? LocationId = null)
    : IRequest<ProductSerialReportDto>, IRequirePermission, IOrganizationScoped, IRequireFeature, ILocationFilteredReport
{
    public string PermissionKey => PermissionKeys.ProductSerialReportView;

    public IReadOnlyCollection<TenantFeature> RequiredFeatures => [TenantFeature.TrackInventory];
}

/// <summary>Read off <c>StockLedgerEntry.QuantityRemaining</c>; see the query's doc comment.</summary>
public enum ProductSerialStatus
{
    InStock = 0,
    Issued = 1,
}

public enum ProductSerialStatusFilter
{
    All = 0,
    InStock = 1,
    Issued = 2,
}

/// <summary>The catalogue's own options for this report, read directly: None / Product / Warehouse.</summary>
public enum ProductSerialGroupBy
{
    None = 0,
    Product = 1,
    Warehouse = 2,
}

/// <summary>One physical unit.</summary>
public sealed record ProductSerialRowDto(
    string SerialNo,
    Guid ProductId,
    string ProductCode,
    string ProductName,
    Guid WarehouseId,
    string WarehouseName,
    ProductSerialStatus Status,
    DateTimeOffset CreatedAt,
    /// <summary>The unit's own cost, which the layer carries for free. Not on the tab -- included
    /// because a serialised item is exactly the case where specific-identification cost is worth
    /// seeing, and because omitting a column the model already holds is a choice too.</summary>
    decimal UnitCost);

public sealed record ProductSerialReportDto(
    DateOnly FromDate,
    DateOnly ToDate,
    IReadOnlyList<ProductSerialRowDto> Items,
    int Page,
    int PageSize,
    int TotalCount,
    /// <summary>Counts over the <b>full filtered set</b>, not the page -- phase-16c bug #1.</summary>
    int InStockCount,
    int IssuedCount);
