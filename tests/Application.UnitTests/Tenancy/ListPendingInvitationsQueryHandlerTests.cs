using ErpApp.Application.Common.Security;
using ErpApp.Application.Tenancy.Commands.AcceptInvitation;
using ErpApp.Application.Tenancy.Commands.InviteUser;
using ErpApp.Application.Tenancy.Queries.ListPendingInvitations;
using ErpApp.Application.UnitTests.TestSupport;
using ErpApp.Domain.Identity;
using ErpApp.Domain.Tenancy;

namespace ErpApp.Application.UnitTests.Tenancy;

/// <summary>
/// The sender's side of an invitation. Reported by the user: an invitation sent to an address with
/// no account, which then registered, showed up nowhere on the sender's screens.
/// </summary>
public class ListPendingInvitationsQueryHandlerTests
{
    [Fact]
    public async Task Lists_an_invitation_sent_before_the_invitee_registered_and_marks_it_registered_after()
    {
        var name = Guid.NewGuid().ToString();
        var (organizationId, adminId) = await SeedAsync(name);

        {
            var db = TestAppDbContext.Create(name);
            await new InviteUserCommandHandler(db, new FakeEmailSender(), new FakeCurrentUserService(adminId))
                .Handle(new InviteUserCommand(organizationId, "Later.Signup@example.com", Role.MemberId), CancellationToken.None);
        }

        var before = await ListAsync(name, organizationId);
        var row = Assert.Single(before);
        Assert.Equal("later.signup@example.com", row.Email);
        Assert.Equal("Member", row.RoleName);
        Assert.False(row.Registered);

        {
            var db = TestAppDbContext.Create(name);
            db.Users.Add(User.Register("Later Signup", "later.signup@example.com", "9800000001", "hashed"));
            await db.SaveChangesAsync();
        }

        var after = Assert.Single(await ListAsync(name, organizationId));
        Assert.Equal(row.MembershipId, after.MembershipId);
        Assert.True(after.Registered);
    }

    [Fact]
    public async Task Leaves_out_accepted_invitations_and_other_organizations()
    {
        var name = Guid.NewGuid().ToString();
        var (organizationId, adminId) = await SeedAsync(name);
        var (otherOrganizationId, otherAdminId) = await SeedAsync(name, "other-co");
        var invitee = User.Register("Jane Doe", "jane@example.com", "9800000002", "hashed");

        {
            var db = TestAppDbContext.Create(name);
            db.Users.Add(invitee);
            await db.SaveChangesAsync();
            await new InviteUserCommandHandler(db, new FakeEmailSender(), new FakeCurrentUserService(adminId))
                .Handle(new InviteUserCommand(organizationId, "jane@example.com", Role.MemberId), CancellationToken.None);
            await new InviteUserCommandHandler(db, new FakeEmailSender(), new FakeCurrentUserService(adminId))
                .Handle(new InviteUserCommand(organizationId, "pending@example.com", Role.MemberId), CancellationToken.None);
            await new InviteUserCommandHandler(db, new FakeEmailSender(), new FakeCurrentUserService(otherAdminId))
                .Handle(new InviteUserCommand(otherOrganizationId, "elsewhere@example.com", Role.MemberId), CancellationToken.None);
        }

        {
            var db = TestAppDbContext.Create(name);
            var janeInvite = db.OrganizationMemberships.Single(m => m.InvitedEmail == "jane@example.com");
            await new AcceptInvitationCommandHandler(db, new FakeCurrentUserService(invitee.Id))
                .Handle(new AcceptInvitationCommand(janeInvite.Id), CancellationToken.None);
        }

        var rows = await ListAsync(name, organizationId);
        Assert.Equal(["pending@example.com"], rows.Select(r => r.Email));
    }

    [Fact]
    public void Is_gated_on_the_invite_key()
    {
        var query = new ListPendingInvitationsQuery(Guid.NewGuid());
        Assert.Equal(PermissionKeys.OrganizationInviteUser, query.PermissionKey);
    }

    private static async Task<IReadOnlyList<PendingSentInvitationDto>> ListAsync(string name, Guid organizationId)
    {
        var db = TestAppDbContext.Create(name);
        return await new ListPendingInvitationsQueryHandler(db)
            .Handle(new ListPendingInvitationsQuery(organizationId), CancellationToken.None);
    }

    private static async Task<(Guid OrganizationId, Guid AdminId)> SeedAsync(string name, string workspace = "acme-traders")
    {
        var db = TestAppDbContext.Create(name);
        var adminId = Guid.NewGuid();
        var organization = Organization.Create(
            "Acme Traders", "Retail", null, new DateOnly(2026, 1, 1), true,
            workspace, null, null, null, null, adminId);
        db.Organizations.Add(organization);
        db.OrganizationMemberships.Add(OrganizationMembership.CreateAccepted(organization.Id, adminId, MembershipRole.Admin));
        if (!db.Roles.Any(r => r.Id == Role.MemberId))
        {
            db.Roles.Add(Role.Create(Role.MemberId, "Member"));
        }

        await db.SaveChangesAsync();
        return (organization.Id, adminId);
    }
}
