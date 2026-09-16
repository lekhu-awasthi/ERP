using ErpApp.Application.Common.Security;
using MediatR;

namespace ErpApp.Application.Catalog.Queries.ListProductSerials;

/// <summary>
/// Phase 51 -- the product detail page's <b>Serial Number</b> tab, read live on 2026-09-16:
/// columns SERIAL NO. / WAREHOUSE / CREATED AT. Eight serials observed (A1, A2, J9, J10, J12, J13,
/// X123, X345), all in Kathmandu.
///
/// <para>Those three columns are the layer's own -- <c>SerialNo</c>, <c>WarehouseId</c>,
/// <c>CreatedAt</c> -- because a serial <i>is</i> a FIFO layer of quantity one. The tab shows only
/// what is in stock, which is <c>QuantityRemaining &gt; 0</c>; the report is where the Issued half
/// is reachable, through its Status filter.</para>
///
/// <para>Rides <c>Catalog.Product.View</c> for the same reason the Batch tab does.</para>
/// </summary>
public sealed record ListProductSerialsQuery(
    Guid OrganizationId,
    Guid ProductId,
    Guid? WarehouseId = null,
    string? Search = null)
    : IRequest<IReadOnlyList<ProductSerialTabRowDto>>, IRequirePermission, IOrganizationScoped
{
    public string PermissionKey => PermissionKeys.ProductView;
}

public sealed record ProductSerialTabRowDto(
    string SerialNo,
    Guid WarehouseId,
    string WarehouseName,
    DateTimeOffset CreatedAt);
