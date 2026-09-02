using ErpApp.Application.Common.Pagination;
using ErpApp.Application.Common.Security;
using ErpApp.Domain.Manufacturing;
using ErpApp.Domain.Tenancy;
using MediatR;

namespace ErpApp.Application.Manufacturing.Queries.ListProductionOrders;

public sealed record ListProductionOrdersQuery(
    Guid OrganizationId,
    ProductionOrderStatus? Status,
    int Page = 1,
    int PageSize = PagingDefaults.DefaultPageSize)
    : IRequest<PagedResult<ProductionOrderListItemDto>>, IRequirePermission, IOrganizationScoped, IRequireFeature
{
    public string PermissionKey => PermissionKeys.ProductionOrderView;

    public IReadOnlyCollection<TenantFeature> RequiredFeatures =>
        [TenantFeature.Manufacturing, TenantFeature.TrackInventory];
}

public sealed record ProductionOrderListItemDto(
    Guid Id, string Code, DateOnly Date, string? Reference, Guid ProductId, string ProductName,
    decimal OutputQuantity, ProductionOrderStatus Status);
