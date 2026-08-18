using ErpApp.Application.Common.Exceptions;
using ErpApp.Application.Tenancy.Commands.InviteUser;
using ErpApp.Application.UnitTests.TestSupport;
using ErpApp.Domain.Identity;
using ErpApp.Domain.Tenancy;

namespace ErpApp.Application.UnitTests.Tenancy;

public class InviteUserCommandHandlerTests
{
    [Fact]
    public async Task Handle_creates_pending_membership_and_sends_invite_email_for_unregistered_email()
    {
        var db = TestAppDbContext.Create();
        var adminId = Guid.NewGuid();
        var organization = Organization.Create(
            "Acme Traders", "Retail", null, new DateOnly(2026, 1, 1), true,
            "acme-traders", null, null, null, null, adminId);
        db.Organizations.Add(organization);
        db.OrganizationMemberships.Add(OrganizationMembership.CreateAccepted(organization.Id, adminId, MembershipRole.Admin));
        db.Roles.Add(Role.Create(Role.MemberId, "Member"));
        await db.SaveChangesAsync();

        var emailSender = new FakeEmailSender();
        var handler = new InviteUserCommandHandler(db, emailSender, new FakeCurrentUserService(adminId));

        var result = await handler.Handle(
            new InviteUserCommand(organization.Id, "New.Hire@example.com", Role.MemberId), CancellationToken.None);

        Assert.Equal("new.hire@example.com", result.Email);
        Assert.Equal("Member", result.RoleName);
        Assert.Single(emailSender.SentEmails);
        Assert.Equal("new.hire@example.com", emailSender.SentEmails[0].To);
    }

    [Fact]
    public async Task Handle_links_invite_to_existing_user_account_by_email()
    {
        var db = TestAppDbContext.Create();
        var adminId = Guid.NewGuid();
        var organization = Organization.Create(
            "Acme Traders", "Retail", null, new DateOnly(2026, 1, 1), true,
            "acme-traders", null, null, null, null, adminId);
        db.Organizations.Add(organization);
        db.OrganizationMemberships.Add(OrganizationMembership.CreateAccepted(organization.Id, adminId, MembershipRole.Admin));
        db.Roles.Add(Role.Create(Role.MemberId, "Member"));
        var existingUser = User.Register("Jane Doe", "jane@example.com", "9800000000", "hashed");
        db.Users.Add(existingUser);
        await db.SaveChangesAsync();

        var handler = new InviteUserCommandHandler(db, new FakeEmailSender(), new FakeCurrentUserService(adminId));

        var result = await handler.Handle(
            new InviteUserCommand(organization.Id, "jane@example.com", Role.MemberId), CancellationToken.None);

        var membership = db.OrganizationMemberships.Single(m => m.Id == result.MembershipId);
        Assert.Equal(existingUser.Id, membership.UserId);
    }

    // Handle_throws_forbidden_when_inviter_is_not_an_admin_of_the_organization moved to
    // AuthorizationBehaviorTests (Phase 1c) -- that check is AuthorizationBehavior's job now,
    // not this handler's; see InviteUserCommandHandler's updated doc comment.

    [Fact]
    public async Task Handle_throws_conflict_when_email_already_invited()
    {
        var db = TestAppDbContext.Create();
        var adminId = Guid.NewGuid();
        var organization = Organization.Create(
            "Acme Traders", "Retail", null, new DateOnly(2026, 1, 1), true,
            "acme-traders", null, null, null, null, adminId);
        db.Organizations.Add(organization);
        db.OrganizationMemberships.Add(OrganizationMembership.CreateAccepted(organization.Id, adminId, MembershipRole.Admin));
        db.Roles.Add(Role.Create(Role.MemberId, "Member"));
        await db.SaveChangesAsync();

        var handler = new InviteUserCommandHandler(db, new FakeEmailSender(), new FakeCurrentUserService(adminId));
        await handler.Handle(new InviteUserCommand(organization.Id, "dup@example.com", Role.MemberId), CancellationToken.None);

        await Assert.ThrowsAsync<ConflictException>(() => handler.Handle(
            new InviteUserCommand(organization.Id, "dup@example.com", Role.MemberId), CancellationToken.None));
    }

    [Fact]
    public async Task Handle_throws_not_found_when_role_does_not_exist()
    {
        var db = TestAppDbContext.Create();
        var adminId = Guid.NewGuid();
        var organization = Organization.Create(
            "Acme Traders", "Retail", null, new DateOnly(2026, 1, 1), true,
            "acme-traders", null, null, null, null, adminId);
        db.Organizations.Add(organization);
        db.OrganizationMemberships.Add(OrganizationMembership.CreateAccepted(organization.Id, adminId, MembershipRole.Admin));
        await db.SaveChangesAsync();

        var handler = new InviteUserCommandHandler(db, new FakeEmailSender(), new FakeCurrentUserService(adminId));

        await Assert.ThrowsAsync<NotFoundException>(() => handler.Handle(
            new InviteUserCommand(organization.Id, "new.hire@example.com", Guid.NewGuid()), CancellationToken.None));
    }
}
