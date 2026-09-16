using ErpApp.Application.Common.Security;
using MediatR;

namespace ErpApp.Application.Catalog.Queries.ListProductBatches;

/// <summary>
/// Phase 51 -- the product detail page's <b>Batch</b> tab, read live on 2026-09-16: columns
/// BATCH NO. / MANUFACTURE DATE / EXPIRY DATE / QUANTITY, with a <i>Select Warehouse</i> filter and
/// a batch search box. Observed row: <c>BATCH123 | 01-09-2026 | 03-09-2026 | 2 CTN</c>.
///
/// <para>Unlike the two reports, this tab <b>was</b> read, so its four columns are the real ones.</para>
///
/// <para><b>No permission key of its own.</b> A batch is read on the product detail page, so it
/// rides <c>Catalog.Product.View</c> -- the same reasoning phase 24 used for variants. There is no
/// batch-management screen to gate, because a batch is created by approving a Purchase Bill.</para>
///
/// <para>QUANTITY is <c>SUM(QuantityRemaining)</c> over the batch's layers, per warehouse: the
/// batch row itself stores no quantity (see <c>ProductBatch</c>), which is what makes this tab and
/// Stock Position incapable of disagreeing.</para>
/// </summary>
public sealed record ListProductBatchesQuery(
    Guid OrganizationId,
    Guid ProductId,
    Guid? WarehouseId = null,
    string? Search = null)
    : IRequest<IReadOnlyList<ProductBatchTabRowDto>>, IRequirePermission, IOrganizationScoped
{
    public string PermissionKey => PermissionKeys.ProductView;
}

public sealed record ProductBatchTabRowDto(
    Guid Id,
    string BatchNo,
    DateOnly? ManufactureDate,
    DateOnly? ExpiryDate,
    decimal Quantity,
    string UnitName,
    /// <summary>Set when the tab is filtered to one warehouse, so the row can say where the
    /// quantity is. Null when the quantity is the batch's total across every warehouse.</summary>
    Guid? WarehouseId,
    string? WarehouseName);
