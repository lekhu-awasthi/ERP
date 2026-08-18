using ErpApp.Application.Common.Security;
using MediatR;

namespace ErpApp.Application.Tenancy.Commands.InviteUser;

/// <summary>
/// <see cref="RoleId"/> replaces the old hardcoded MembershipRole selector (Phase 14, Role
/// Reference) -- the inviter now picks from ListRolesQuery's full set (the two shared system
/// roles plus this Organization's own custom ones), not just Admin/Member. The handler validates
/// RoleId resolves to a role this Organization is actually allowed to assign before creating the
/// membership.
/// </summary>
public sealed record InviteUserCommand(Guid OrganizationId, string Email, Guid RoleId)
    : IRequest<InviteUserResult>, IRequirePermission, IOrganizationScoped
{
    public string PermissionKey => PermissionKeys.OrganizationInviteUser;
}

public sealed record InviteUserResult(Guid MembershipId, string Email, Guid RoleId, string RoleName);
