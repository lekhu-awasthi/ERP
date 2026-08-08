namespace ErpApp.Domain.Tenancy;

/// <summary>
/// Join entity linking a User to an Organization (architecture-spec.md §2/§4.1). Backs all
/// three tabs of the "Your Organizations / Requests / Invitations" landing page: an Accepted
/// row is a listed Organization, a Requested row (this user asked to join) powers Requests, an
/// Invited row (an admin invited this email) powers Invitations.
///
/// <see cref="UserId"/> is nullable because an invite is addressed to an email address, not
/// necessarily an existing account (confirmed live: Tigg's UserInvitation is email-based) --
/// it's resolved and filled in at accept time via <see cref="AcceptAsUser"/> if the invited
/// person didn't have an account yet when invited.
/// </summary>
public sealed class OrganizationMembership
{
    public Guid Id { get; private set; }
    public Guid OrganizationId { get; private set; }
    public Guid? UserId { get; private set; }
    public string? InvitedEmail { get; private set; }
    public MembershipRole Role { get; private set; }
    public MembershipStatus Status { get; private set; }
    public Guid? InvitedByUserId { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? AcceptedAt { get; private set; }

    private OrganizationMembership()
    {
    }

    /// <summary>The auto-created Admin-equivalent membership for whoever creates an Organization.</summary>
    public static OrganizationMembership CreateAccepted(Guid organizationId, Guid userId, MembershipRole role)
    {
        var now = DateTimeOffset.UtcNow;

        return new OrganizationMembership
        {
            Id = Guid.NewGuid(),
            OrganizationId = organizationId,
            UserId = userId,
            Role = role,
            Status = MembershipStatus.Accepted,
            CreatedAt = now,
            AcceptedAt = now,
        };
    }

    /// <summary>
    /// A logged-in user requesting to join an existing Organization. No command originates this
    /// yet (Phase 1b has no "browse/search workspaces" UI), but the factory exists so the
    /// Requested state (and Accept()'s handling of it) is a real, constructible, testable path
    /// rather than dead code the enum merely gestures at.
    /// </summary>
    public static OrganizationMembership Request(Guid organizationId, Guid userId, MembershipRole role)
    {
        return new OrganizationMembership
        {
            Id = Guid.NewGuid(),
            OrganizationId = organizationId,
            UserId = userId,
            Role = role,
            Status = MembershipStatus.Requested,
            CreatedAt = DateTimeOffset.UtcNow,
        };
    }

    public static OrganizationMembership Invite(
        Guid organizationId, Guid? userId, string invitedEmail, MembershipRole role, Guid invitedByUserId)
    {
        return new OrganizationMembership
        {
            Id = Guid.NewGuid(),
            OrganizationId = organizationId,
            UserId = userId,
            InvitedEmail = invitedEmail,
            Role = role,
            Status = MembershipStatus.Invited,
            InvitedByUserId = invitedByUserId,
            CreatedAt = DateTimeOffset.UtcNow,
        };
    }

    /// <summary>The invitee accepting their own invitation -- resolves <see cref="UserId"/> if it wasn't set yet.</summary>
    public void AcceptAsUser(Guid userId)
    {
        if (Status != MembershipStatus.Invited)
        {
            return;
        }

        UserId = userId;
        Status = MembershipStatus.Accepted;
        AcceptedAt = DateTimeOffset.UtcNow;
    }

    /// <summary>An org admin approving someone else's pending join request.</summary>
    public void Accept()
    {
        if (Status != MembershipStatus.Requested)
        {
            return;
        }

        Status = MembershipStatus.Accepted;
        AcceptedAt = DateTimeOffset.UtcNow;
    }
}
