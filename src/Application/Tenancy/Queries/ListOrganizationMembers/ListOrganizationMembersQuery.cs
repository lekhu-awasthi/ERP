using ErpApp.Application.Common.Pagination;
using ErpApp.Application.Common.Security;
using MediatR;

namespace ErpApp.Application.Tenancy.Queries.ListOrganizationMembers;

/// <summary>
/// Added for Phase 13 (Tasks) -- the only current consumer is the Task feature's Assigned-To picker
/// (CreateTaskCommand/UpdateTaskCommand validate AssignedToUserId against exactly this same
/// Accepted-membership set, see WorkflowValidation.EnsureAssigneeIsAcceptedMemberAsync), so this is
/// gated on PermissionKeys.TaskView rather than minting a standalone Tenancy-level "view members"
/// key that nothing else needs yet.
/// </summary>
public sealed record ListOrganizationMembersQuery(
    Guid OrganizationId, int Page = 1, int PageSize = PagingDefaults.MaxPageSize)
    : IRequest<PagedResult<OrganizationMemberDto>>, IRequirePermission, IOrganizationScoped
{
    public string PermissionKey => PermissionKeys.TaskView;
}

/// <summary>
/// MembershipId (Phase 14) lets the Role Reference page's Members section reassign a member's
/// Role via UpdateMembershipRoleCommand, which targets a membership row, not a bare UserId.
/// </summary>
public sealed record OrganizationMemberDto(Guid MembershipId, Guid UserId, string FullName, string Email, Guid RoleId, string RoleName);
