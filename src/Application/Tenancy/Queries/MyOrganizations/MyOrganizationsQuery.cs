using ErpApp.Domain.Tenancy;
using MediatR;

namespace ErpApp.Application.Tenancy.Queries.MyOrganizations;

/// <summary>Powers the "Your Organizations / Requests / Invitations" 3-tab landing page (PRD FR-1.3).</summary>
public sealed record MyOrganizationsQuery : IRequest<MyOrganizationsResult>;

public sealed record OrganizationSummaryDto(Guid OrganizationId, string Name, string WorkspaceName, string Industry, MembershipRole Role);

public sealed record PendingRequestDto(Guid MembershipId, Guid OrganizationId, string OrganizationName, DateTimeOffset RequestedAt);

public sealed record PendingInvitationDto(
    Guid MembershipId, Guid OrganizationId, string OrganizationName, MembershipRole Role, DateTimeOffset InvitedAt);

public sealed record MyOrganizationsResult(
    IReadOnlyList<OrganizationSummaryDto> Organizations,
    IReadOnlyList<PendingRequestDto> Requests,
    IReadOnlyList<PendingInvitationDto> Invitations);
