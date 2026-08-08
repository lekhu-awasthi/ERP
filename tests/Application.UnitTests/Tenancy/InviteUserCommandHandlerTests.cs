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
        await db.SaveChangesAsync();

        var emailSender = new FakeEmailSender();
        var handler = new InviteUserCommandHandler(db, emailSender, new FakeCurrentUserService(adminId));

        var result = await handler.Handle(
            new InviteUserCommand(organization.Id, "New.Hire@example.com", MembershipRole.Member), CancellationToken.None);

        Assert.Equal("new.hire@example.com", result.Email);
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
        var existingUser = User.Register("Jane Doe", "jane@example.com", "9800000000", "hashed");
        db.Users.Add(existingUser);
        await db.SaveChangesAsync();

        var handler = new InviteUserCommandHandler(db, new FakeEmailSender(), new FakeCurrentUserService(adminId));

        var result = await handler.Handle(
            new InviteUserCommand(organization.Id, "jane@example.com", MembershipRole.Member), CancellationToken.None);

        var membership = db.OrganizationMemberships.Single(m => m.Id == result.MembershipId);
        Assert.Equal(existingUser.Id, membership.UserId);
    }

    [Fact]
    public async Task Handle_throws_forbidden_when_inviter_is_not_an_admin_of_the_organization()
    {
        var db = TestAppDbContext.Create();
        var adminId = Guid.NewGuid();
        var organization = Organization.Create(
            "Acme Traders", "Retail", null, new DateOnly(2026, 1, 1), true,
            "acme-traders", null, null, null, null, adminId);
        db.Organizations.Add(organization);
        db.OrganizationMemberships.Add(OrganizationMembership.CreateAccepted(organization.Id, adminId, MembershipRole.Admin));
        await db.SaveChangesAsync();

        var strangerId = Guid.NewGuid();
        var handler = new InviteUserCommandHandler(db, new FakeEmailSender(), new FakeCurrentUserService(strangerId));

        await Assert.ThrowsAsync<ForbiddenException>(() => handler.Handle(
            new InviteUserCommand(organization.Id, "someone@example.com", MembershipRole.Member), CancellationToken.None));
    }

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
        await db.SaveChangesAsync();

        var handler = new InviteUserCommandHandler(db, new FakeEmailSender(), new FakeCurrentUserService(adminId));
        await handler.Handle(new InviteUserCommand(organization.Id, "dup@example.com", MembershipRole.Member), CancellationToken.None);

        await Assert.ThrowsAsync<ConflictException>(() => handler.Handle(
            new InviteUserCommand(organization.Id, "dup@example.com", MembershipRole.Member), CancellationToken.None));
    }
}
