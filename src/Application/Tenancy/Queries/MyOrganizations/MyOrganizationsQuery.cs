using MediatR;

namespace ErpApp.Application.Tenancy.Queries.MyOrganizations;

/// <summary>Powers the "Your Organizations / Requests / Invitations" 3-tab landing page (PRD FR-1.3).</summary>
public sealed record MyOrganizationsQuery : IRequest<MyOrganizationsResult>;

/// <summary>
/// Role is the Role entity's Name ("Admin"/"Member") joined in by the handler, not the old
/// MembershipRole enum -- Phase 1c moved OrganizationMembership's persisted role to a RoleId FK
/// (see Role's doc comment), but the DTO shape (and thus the Angular contract) stays a plain
/// string so organization-dashboard-page's `org.role === 'Admin'` check needs no changes.
/// </summary>
public sealed record OrganizationSummaryDto(Guid OrganizationId, string Name, string WorkspaceName, string Industry, string Role);

public sealed record PendingRequestDto(Guid MembershipId, Guid OrganizationId, string OrganizationName, DateTimeOffset RequestedAt);

public sealed record PendingInvitationDto(
    Guid MembershipId, Guid OrganizationId, string OrganizationName, string Role, DateTimeOffset InvitedAt);

public sealed record MyOrganizationsResult(
    IReadOnlyList<OrganizationSummaryDto> Organizations,
    IReadOnlyList<PendingRequestDto> Requests,
    IReadOnlyList<PendingInvitationDto> Invitations);
