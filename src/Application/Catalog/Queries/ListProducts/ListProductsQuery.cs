using ErpApp.Application.Common.Security;
using ErpApp.Domain.Catalog;
using MediatR;

namespace ErpApp.Application.Catalog.Queries.ListProducts;

public sealed record ListProductsQuery(Guid OrganizationId, ProductType? Type)
    : IRequest<IReadOnlyList<Product>>, IRequirePermission, IOrganizationScoped
{
    public string PermissionKey => PermissionKeys.ProductView;
}
