using ErpApp.Application.Common.Security;
using MediatR;

namespace ErpApp.Application.Catalog.Commands.UpdateProductCategory;

public sealed record UpdateProductCategoryCommand(Guid OrganizationId, Guid Id, string Name, Guid? ParentCategoryId, bool IsActive)
    : IRequest<UpdateProductCategoryResult>, IRequirePermission, IOrganizationScoped
{
    public string PermissionKey => PermissionKeys.ProductCategoryManage;
}

public sealed record UpdateProductCategoryResult(Guid Id, string Name, Guid? ParentCategoryId, bool IsActive);
