using ErpApp.Application.Common.Security;
using MediatR;

namespace ErpApp.Application.Tenancy.Commands.DeleteRole;

public sealed record DeleteRoleCommand(Guid OrganizationId, Guid Id) : IRequest<Unit>, IRequirePermission, IOrganizationScoped
{
    public string PermissionKey => PermissionKeys.RoleManage;
}
