using ErpApp.Application.Common.Filtering;
using ErpApp.Application.Common.Pagination;
using ErpApp.Application.Common.Security;
using ErpApp.Domain.Catalog;
using MediatR;

namespace ErpApp.Application.Catalog.Queries.ListProducts;

/// <param name="LocationId">Phase 36 -- narrows the list to products available at one billing
/// location: those restricted to it, plus every product carrying no restriction at all. Null is
/// "every product", which is what the Products grid always asks for and what every caller before
/// this phase got. The document line pickers pass their document's own location, which is what the
/// reference product does -- one <c>products-minimized?…&amp;location_id=…</c> call per document,
/// re-issued when the header location changes (confirmed live 2026-09-11).</param>
public sealed record ListProductsQuery(
    Guid OrganizationId,
    ProductType? Type,
    ProductVariantFilter VariantFilter = ProductVariantFilter.All,
    int Page = 1,
    int PageSize = PagingDefaults.DefaultPageSize,
    string? Search = null,
    Guid? LocationId = null)
    : IRequest<PagedResult<Product>>, IRequirePermission, IOrganizationScoped, ISearchableQuery
{
    public string PermissionKey => PermissionKeys.ProductView;
}
