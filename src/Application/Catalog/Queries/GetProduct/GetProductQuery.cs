using ErpApp.Application.Common.Security;
using ErpApp.Domain.Catalog;
using MediatR;

namespace ErpApp.Application.Catalog.Queries.GetProduct;

public sealed record GetProductQuery(Guid OrganizationId, Guid Id)
    : IRequest<Product>, IRequirePermission, IOrganizationScoped
{
    public string PermissionKey => PermissionKeys.ProductView;
}
