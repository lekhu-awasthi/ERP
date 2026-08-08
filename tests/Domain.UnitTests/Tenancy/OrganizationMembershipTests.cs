using ErpApp.Domain.Tenancy;

namespace ErpApp.Domain.UnitTests.Tenancy;

public class OrganizationMembershipTests
{
    [Fact]
    public void CreateAccepted_starts_accepted_with_matching_user()
    {
        var organizationId = Guid.NewGuid();
        var userId = Guid.NewGuid();

        var membership = OrganizationMembership.CreateAccepted(organizationId, userId, MembershipRole.Admin);

        Assert.Equal(MembershipStatus.Accepted, membership.Status);
        Assert.Equal(userId, membership.UserId);
        Assert.NotNull(membership.AcceptedAt);
    }

    [Fact]
    public void Invite_starts_invited_with_null_user_when_no_account_exists_yet()
    {
        var membership = OrganizationMembership.Invite(
            Guid.NewGuid(), userId: null, "invitee@example.com", MembershipRole.Member, Guid.NewGuid());

        Assert.Equal(MembershipStatus.Invited, membership.Status);
        Assert.Null(membership.UserId);
        Assert.Equal("invitee@example.com", membership.InvitedEmail);
    }

    [Fact]
    public void AcceptAsUser_resolves_user_id_and_transitions_to_accepted()
    {
        var membership = OrganizationMembership.Invite(
            Guid.NewGuid(), userId: null, "invitee@example.com", MembershipRole.Member, Guid.NewGuid());
        var acceptingUserId = Guid.NewGuid();

        membership.AcceptAsUser(acceptingUserId);

        Assert.Equal(MembershipStatus.Accepted, membership.Status);
        Assert.Equal(acceptingUserId, membership.UserId);
        Assert.NotNull(membership.AcceptedAt);
    }

    [Fact]
    public void AcceptAsUser_is_a_no_op_once_already_accepted()
    {
        var membership = OrganizationMembership.CreateAccepted(Guid.NewGuid(), Guid.NewGuid(), MembershipRole.Admin);
        var originalAcceptedAt = membership.AcceptedAt;

        membership.AcceptAsUser(Guid.NewGuid());

        Assert.Equal(originalAcceptedAt, membership.AcceptedAt);
    }

    [Fact]
    public void Accept_transitions_requested_membership_to_accepted()
    {
        var membership = OrganizationMembership.Request(Guid.NewGuid(), Guid.NewGuid(), MembershipRole.Member);

        membership.Accept();

        Assert.Equal(MembershipStatus.Accepted, membership.Status);
        Assert.NotNull(membership.AcceptedAt);
    }

    [Fact]
    public void Accept_is_a_no_op_for_a_status_other_than_requested()
    {
        var membership = OrganizationMembership.Invite(
            Guid.NewGuid(), userId: null, "invitee@example.com", MembershipRole.Member, Guid.NewGuid());

        membership.Accept();

        Assert.Equal(MembershipStatus.Invited, membership.Status);
    }
}
