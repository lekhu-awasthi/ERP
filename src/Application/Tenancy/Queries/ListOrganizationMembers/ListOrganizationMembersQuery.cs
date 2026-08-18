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
public sealed record ListOrganizationMembersQuery(Guid OrganizationId)
    : IRequest<IReadOnlyList<OrganizationMemberDto>>, IRequirePermission, IOrganizationScoped
{
    public string PermissionKey => PermissionKeys.TaskView;
}

public sealed record OrganizationMemberDto(Guid UserId, string FullName, string Email, string RoleName);
