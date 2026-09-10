using ErpApp.Application.Common.Filtering;
using ErpApp.Application.Common.Pagination;
using ErpApp.Application.Common.Security;
using ErpApp.Domain.Catalog;
using MediatR;

namespace ErpApp.Application.Catalog.Queries.ListProducts;

public sealed record ListProductsQuery(
    Guid OrganizationId,
    ProductType? Type,
    ProductVariantFilter VariantFilter = ProductVariantFilter.All,
    int Page = 1,
    int PageSize = PagingDefaults.DefaultPageSize,
    string? Search = null)
    : IRequest<PagedResult<Product>>, IRequirePermission, IOrganizationScoped, ISearchableQuery
{
    public string PermissionKey => PermissionKeys.ProductView;
}
