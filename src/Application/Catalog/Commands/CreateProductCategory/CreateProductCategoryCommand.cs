using ErpApp.Application.Common.Security;
using MediatR;

namespace ErpApp.Application.Catalog.Commands.CreateProductCategory;

public sealed record CreateProductCategoryCommand(Guid OrganizationId, string Name, Guid? ParentCategoryId)
    : IRequest<CreateProductCategoryResult>, IRequirePermission, IOrganizationScoped
{
    public string PermissionKey => PermissionKeys.ProductCategoryManage;
}

public sealed record CreateProductCategoryResult(Guid Id, string Name, Guid? ParentCategoryId);
