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
/// <param name="TermEndsAt">Phase 49 -- the end of this tenant's current subscription term, or
/// <c>null</c> for a tenant with no subscription row at all (a fixture, or a partially migrated
/// tenant -- <c>SubscriptionExpiryBehavior</c> treats that case as live and so does this).</param>
/// <param name="IsExpired">Phase 49 -- whether that term has already ended, i.e. whether
/// <c>SubscriptionExpiryBehavior</c> will refuse every document write inside this organization.
///
/// <para><b>This is where phase 49's divergence from the reference product is visible.</b> Read live
/// on 2026-09-16, the reference product answers the equivalent request
/// (<c>GET /api/v1/me/namespaces</c>) for an expired trial with <c>total: 0</c> and an empty list --
/// the organization is simply absent, and the portal then shows its first-run onboarding modal. This
/// product deliberately keeps the row and marks it instead; see docs/phase-49-status.md Decision A
/// for the reasoning, which turns on the read being one sample of <i>trial</i> behaviour and zero
/// samples of paid behaviour.</para>
///
/// <para>Computed from the term end and the clock rather than stored, for
/// <c>SubscriptionUsageReader</c>'s reason: a second writer of the same fact is a second chance to
/// disagree with the behavior that enforces it.</para></param>
public sealed record OrganizationSummaryDto(
    Guid OrganizationId,
    string Name,
    string WorkspaceName,
    string Industry,
    string Role,
    DateTimeOffset? TermEndsAt,
    bool IsExpired);

public sealed record PendingRequestDto(Guid MembershipId, Guid OrganizationId, string OrganizationName, DateTimeOffset RequestedAt);

public sealed record PendingInvitationDto(
    Guid MembershipId, Guid OrganizationId, string OrganizationName, string Role, DateTimeOffset InvitedAt);

public sealed record MyOrganizationsResult(
    IReadOnlyList<OrganizationSummaryDto> Organizations,
    IReadOnlyList<PendingRequestDto> Requests,
    IReadOnlyList<PendingInvitationDto> Invitations);
