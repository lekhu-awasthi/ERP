using ErpApp.Application.Common.Security;
using MediatR;

namespace ErpApp.Application.Tenancy.Queries.ListPendingInvitations;

/// <summary>
/// The invitations an Organization has sent that nobody has accepted yet, for the sender's side of
/// the invite panel. Before this, a sent invitation was visible only on the invitee's own
/// Invitations tab; the sender saw "Invitation sent" once and then nothing, and
/// ListOrganizationMembersQuery lists Accepted rows only, so an unanswered invitation looked lost.
///
/// Gated on the invite key itself: whoever may send an invitation may see what they sent, and the
/// rows are email addresses, which the key-derivation rule keeps Admin-only anyway. Not paged: an
/// Organization's unanswered invitations are a handful, and the panel shows all of them.
/// </summary>
public sealed record ListPendingInvitationsQuery(Guid OrganizationId)
    : IRequest<IReadOnlyList<PendingSentInvitationDto>>, IRequirePermission, IOrganizationScoped
{
    public string PermissionKey => PermissionKeys.OrganizationInviteUser;
}

/// <summary>
/// <see cref="Registered"/> says whether an account exists for the email yet, which is the
/// question a sender asks when the invitee says they see nothing: an unregistered address has
/// nowhere to see it until it signs up with exactly this email.
/// </summary>
public sealed record PendingSentInvitationDto(
    Guid MembershipId, string Email, Guid RoleId, string RoleName, DateTimeOffset SentAt, bool Registered);
