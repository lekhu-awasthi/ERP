using ErpApp.Application.Common.Security;
using MediatR;

namespace ErpApp.Application.Tenancy.Commands.CreateRole;

/// <summary>
/// Creates a tenant's own custom Role (Phase 14, Role Reference) -- a brand-new row starts with
/// zero RolePermission grants at all (GetRolePermissionMatrixQueryHandler defaults every key with
/// no row to IsGranted=false), so an Admin explicitly grants only what the role needs via
/// UpdateRolePermissionsCommand afterward, rather than inheriting anything from Admin or Member.
/// </summary>
public sealed record CreateRoleCommand(Guid OrganizationId, string Name, string? Description)
    : IRequest<CreateRoleResult>, IRequirePermission, IOrganizationScoped
{
    public string PermissionKey => PermissionKeys.RoleManage;
}

public sealed record CreateRoleResult(Guid Id, string Name, string? Description);
