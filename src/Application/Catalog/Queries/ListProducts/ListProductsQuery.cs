using ErpApp.Application.Common.Pagination;
using ErpApp.Application.Common.Security;
using ErpApp.Domain.Catalog;
using MediatR;

namespace ErpApp.Application.Catalog.Queries.ListProducts;

public sealed record ListProductsQuery(
    Guid OrganizationId,
    ProductType? Type,
    int Page = 1,
    int PageSize = PagingDefaults.DefaultPageSize)
    : IRequest<PagedResult<Product>>, IRequirePermission, IOrganizationScoped
{
    public string PermissionKey => PermissionKeys.ProductView;
}
