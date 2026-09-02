using ErpApp.Application.Common.Pagination;
using ErpApp.Application.Common.Security;
using ErpApp.Domain.Tenancy;
using MediatR;

namespace ErpApp.Application.Manufacturing.Queries.ListBillsOfMaterials;

public sealed record ListBillsOfMaterialsQuery(
    Guid OrganizationId,
    string? Search,
    bool? IsActive,
    int Page = 1,
    int PageSize = PagingDefaults.DefaultPageSize)
    : IRequest<PagedResult<BillOfMaterialsListItemDto>>, IRequirePermission, IOrganizationScoped, IRequireFeature
{
    public string PermissionKey => PermissionKeys.BillOfMaterialsView;

    public IReadOnlyCollection<TenantFeature> RequiredFeatures =>
        [TenantFeature.Manufacturing, TenantFeature.TrackInventory];
}

/// <summary>Mirrors the reference product's own BOM list columns: product, the finished output
/// quantity with its unit, and a count of raw materials and by-products.</summary>
public sealed record BillOfMaterialsListItemDto(
    Guid Id,
    Guid ProductId,
    string ProductName,
    string ProductCode,
    string? UnitName,
    decimal OutputQuantity,
    int RawMaterialCount,
    int ByProductCount,
    bool ManufactureOnEverySale,
    bool IsActive);
