using ErpApp.Application.Common.Locations;
using ErpApp.Application.Common.Pagination;
using ErpApp.Application.Common.Security;
using ErpApp.Domain.Tenancy;
using MediatR;

namespace ErpApp.Application.Inventory.Queries.ProductBatchReport;

/// <summary>
/// The Inventory Report group's <b>Product Batch Report</b> (phase 51, reference slug
/// <c>batch-tracking-report</c>).
///
/// <para><b>Its column set was never read, and that is recorded here rather than only in a status
/// doc.</b> The 2026-09-16 pass got <c>permission denied</c> on this report and on its serial
/// sibling, because the vendor gates both behind new permission keys the demo Admin does not hold.
/// Phase 51 asked whether to obtain an account that holds them and the answer was to design from
/// the product tabs instead -- the phase-8f rule, invoked explicitly. So what this returns is
/// derived from two things that <i>were</i> readable:</para>
/// <list type="bullet">
/// <item>the product detail <b>Batch tab</b> -- columns BATCH NO. / MANUFACTURE DATE / EXPIRY DATE /
/// QUANTITY, with a Select Warehouse filter, and the observed row
/// <c>BATCH123 | 01-09-2026 | 03-09-2026 | 2 CTN</c>;</item>
/// <item>the <b>catalogue's own filter list</b> for this report, which was readable even though the
/// report was not: Period, Group By, Warehouse.</item>
/// </list>
///
/// <para>Column order, grouping behaviour and totals may therefore differ from the vendor's. What
/// they are not is invented: every column traces to a column on that tab, and the report adds the
/// Product identification any tenant-wide (rather than one-product) view obviously needs.</para>
///
/// <para><b>Quantity is a GROUP BY, not a stored figure.</b> <c>ProductBatch</c> holds no quantity
/// -- see its doc comment -- so a batch's on-hand comes from summing the FIFO layers that carry its
/// id. That is the phase's central decision and the reason this report cannot disagree with Stock
/// Position: there is one quantity in the system and this groups it.</para>
///
/// <para><b>Period means received-in-period</b>, and it has to be said out loud because a batch is
/// not itself dated by a transaction. A batch's rows are its layers, and a layer's
/// <c>TransactionDate</c> is its source document's business date -- so the filter admits batches
/// that had stock <i>received</i> inside the window. Quantity remains the live on-hand, exactly as
/// the tab shows it, because <c>QuantityRemaining</c> is decremented in place and only ever answers
/// "as of now" (phase 26c). A dated as-at balance would have to come from <c>StockMovement</c>, and
/// nothing read says this report is an as-at report.</para>
/// </summary>
public sealed record ProductBatchReportQuery(
    Guid OrganizationId,
    DateOnly FromDate,
    DateOnly ToDate,
    Guid? ProductId,
    Guid? WarehouseId,
    ProductBatchGroupBy GroupBy = ProductBatchGroupBy.None,
    int Page = 1,
    int PageSize = PagingDefaults.DefaultPageSize,
    bool ExportAll = false,
    Guid? LocationId = null)
    : IRequest<ProductBatchReportDto>, IRequirePermission, IOrganizationScoped, IRequireFeature, ILocationFilteredReport
{
    public string PermissionKey => PermissionKeys.ProductBatchReportView;

    public IReadOnlyCollection<TenantFeature> RequiredFeatures => [TenantFeature.TrackInventory];
}

/// <summary>
/// The catalogue's Group By options for both traceability reports, read from the serial report's
/// own control (None / Product / Warehouse) and applied to the batch report too, whose catalogue
/// entry shows the same filter without its options being readable.
///
/// <para>That extrapolation is a guess and is labelled as one. It is the mild kind: the two reports
/// are siblings in the same group with the same filter name, and the three options are the only
/// dimensions either report has.</para>
/// </summary>
public enum ProductBatchGroupBy
{
    None = 0,
    Product = 1,
    Warehouse = 2,
}

/// <summary>One batch, in one warehouse when grouped that way, with its live on-hand.</summary>
public sealed record ProductBatchRowDto(
    Guid BatchId,
    string BatchNo,
    Guid ProductId,
    string ProductCode,
    string ProductName,
    DateOnly? ManufactureDate,
    DateOnly? ExpiryDate,
    Guid? WarehouseId,
    string? WarehouseName,
    decimal Quantity,
    string UnitName);

public sealed record ProductBatchReportDto(
    DateOnly FromDate,
    DateOnly ToDate,
    IReadOnlyList<ProductBatchRowDto> Items,
    int Page,
    int PageSize,
    int TotalCount,
    /// <summary>
    /// The footer total over the <b>full filtered set</b>, not a reduce over one page -- phase-16c
    /// bug #1, which is a server-computed field precisely so a paginated screen cannot show a total
    /// of the rows it happens to be displaying.
    /// </summary>
    decimal TotalQuantity);
