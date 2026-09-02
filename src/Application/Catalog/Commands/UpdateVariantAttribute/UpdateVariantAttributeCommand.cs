using ErpApp.Application.Catalog.Commands.CreateVariantAttribute;
using ErpApp.Application.Common.Security;
using MediatR;

namespace ErpApp.Application.Catalog.Commands.UpdateVariantAttribute;

public sealed record UpdateVariantAttributeCommand(Guid OrganizationId, Guid Id, string Name, bool IsActive)
    : IRequest<VariantAttributeResult>, IRequirePermission, IOrganizationScoped
{
    public string PermissionKey => PermissionKeys.VariantAttributeManage;
}
