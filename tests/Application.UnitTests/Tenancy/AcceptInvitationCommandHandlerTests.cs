using ErpApp.Application.Common.Exceptions;
using ErpApp.Application.Tenancy.Commands.AcceptInvitation;
using ErpApp.Application.UnitTests.TestSupport;
using ErpApp.Domain.Identity;
using ErpApp.Domain.Tenancy;

namespace ErpApp.Application.UnitTests.Tenancy;

public class AcceptInvitationCommandHandlerTests
{
    [Fact]
    public async Task Handle_accepts_invitation_addressed_to_the_current_user_by_user_id()
    {
        var db = TestAppDbContext.Create();
        var invitee = User.Register("Jane Doe", "jane@example.com", "9800000000", "hashed");
        db.Users.Add(invitee);
        var organization = Organization.Create(
            "Acme Traders", "Retail", null, new DateOnly(2026, 1, 1), true,
            "acme-traders", null, null, null, null, Guid.NewGuid());
        db.Organizations.Add(organization);
        var membership = OrganizationMembership.Invite(
            organization.Id, invitee.Id, invitee.Email, MembershipRole.Member, Guid.NewGuid());
        db.OrganizationMemberships.Add(membership);
        await db.SaveChangesAsync();

        var handler = new AcceptInvitationCommandHandler(db, new FakeCurrentUserService(invitee.Id));
        await handler.Handle(new AcceptInvitationCommand(membership.Id), CancellationToken.None);

        Assert.Equal(MembershipStatus.Accepted, membership.Status);
    }

    [Fact]
    public async Task Handle_accepts_invitation_addressed_by_email_when_no_user_was_linked_at_invite_time()
    {
        var db = TestAppDbContext.Create();
        var invitee = User.Register("Jane Doe", "jane@example.com", "9800000000", "hashed");
        db.Users.Add(invitee);
        var organization = Organization.Create(
            "Acme Traders", "Retail", null, new DateOnly(2026, 1, 1), true,
            "acme-traders", null, null, null, null, Guid.NewGuid());
        db.Organizations.Add(organization);
        var membership = OrganizationMembership.Invite(
            organization.Id, userId: null, invitee.Email, MembershipRole.Member, Guid.NewGuid());
        db.OrganizationMemberships.Add(membership);
        await db.SaveChangesAsync();

        var handler = new AcceptInvitationCommandHandler(db, new FakeCurrentUserService(invitee.Id));
        await handler.Handle(new AcceptInvitationCommand(membership.Id), CancellationToken.None);

        Assert.Equal(MembershipStatus.Accepted, membership.Status);
        Assert.Equal(invitee.Id, membership.UserId);
    }

    [Fact]
    public async Task Handle_throws_forbidden_when_invitation_addressed_to_someone_else()
    {
        var db = TestAppDbContext.Create();
        var invitee = User.Register("Jane Doe", "jane@example.com", "9800000000", "hashed");
        var stranger = User.Register("Bob Stranger", "bob@example.com", "9800000001", "hashed");
        db.Users.AddRange(invitee, stranger);
        var organization = Organization.Create(
            "Acme Traders", "Retail", null, new DateOnly(2026, 1, 1), true,
            "acme-traders", null, null, null, null, Guid.NewGuid());
        db.Organizations.Add(organization);
        var membership = OrganizationMembership.Invite(
            organization.Id, userId: null, invitee.Email, MembershipRole.Member, Guid.NewGuid());
        db.OrganizationMemberships.Add(membership);
        await db.SaveChangesAsync();

        var handler = new AcceptInvitationCommandHandler(db, new FakeCurrentUserService(stranger.Id));

        await Assert.ThrowsAsync<ForbiddenException>(() => handler.Handle(
            new AcceptInvitationCommand(membership.Id), CancellationToken.None));
    }

    [Fact]
    public async Task Handle_throws_not_found_for_unknown_membership()
    {
        var db = TestAppDbContext.Create();
        db.Users.Add(User.Register("Jane Doe", "jane@example.com", "9800000000", "hashed"));
        await db.SaveChangesAsync();

        var handler = new AcceptInvitationCommandHandler(db, new FakeCurrentUserService(Guid.NewGuid()));

        await Assert.ThrowsAsync<NotFoundException>(() => handler.Handle(
            new AcceptInvitationCommand(Guid.NewGuid()), CancellationToken.None));
    }
}
