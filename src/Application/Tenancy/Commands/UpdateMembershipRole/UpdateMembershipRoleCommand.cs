using ErpApp.Application.Common.Security;
using MediatR;

namespace ErpApp.Application.Tenancy.Commands.UpdateMembershipRole;

/// <summary>
/// Reassigns an existing Accepted member's Role (the Users tab, Phase 14) -- until now a
/// member's Role was fixed at invite time (InviteUserCommand) with no way to change it after
/// acceptance.
/// </summary>
public sealed record UpdateMembershipRoleCommand(Guid OrganizationId, Guid MembershipId, Guid RoleId)
    : IRequest<Unit>, IRequirePermission, IOrganizationScoped
{
    public string PermissionKey => PermissionKeys.RoleManage;
}
