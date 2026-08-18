using ErpApp.Application.Common.Security;
using MediatR;

namespace ErpApp.Application.Tenancy.Commands.UpdateRole;

public sealed record UpdateRoleCommand(Guid OrganizationId, Guid Id, string Name, string? Description)
    : IRequest<UpdateRoleResult>, IRequirePermission, IOrganizationScoped
{
    public string PermissionKey => PermissionKeys.RoleManage;
}

public sealed record UpdateRoleResult(Guid Id, string Name, string? Description);
