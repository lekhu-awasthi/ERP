using ErpApp.Application.Tenancy.Queries.MyOrganizations;
using ErpApp.Application.UnitTests.TestSupport;
using ErpApp.Domain.Identity;
using ErpApp.Domain.Tenancy;

namespace ErpApp.Application.UnitTests.Tenancy;

public class MyOrganizationsQueryHandlerTests
{
    [Fact]
    public async Task Handle_returns_accepted_organizations_pending_requests_and_pending_invitations_separately()
    {
        var db = TestAppDbContext.Create();
        var currentUser = User.Register("Jane Doe", "jane@example.com", "9800000000", "hashed");
        var currentUserId = currentUser.Id;
        db.Users.Add(currentUser);

        var acceptedOrg = Organization.Create(
            "Acme Traders", "Retail", null, new DateOnly(2026, 1, 1), true,
            "acme-traders", null, null, null, null, currentUserId);
        var requestedOrg = Organization.Create(
            "Other Org", "Manufacturing", null, new DateOnly(2026, 1, 1), false,
            "other-org", null, null, null, null, Guid.NewGuid());
        var invitedOrg = Organization.Create(
            "Third Org", "Services", null, new DateOnly(2026, 1, 1), false,
            "third-org", null, null, null, null, Guid.NewGuid());
        db.Organizations.AddRange(acceptedOrg, requestedOrg, invitedOrg);

        db.OrganizationMemberships.Add(OrganizationMembership.CreateAccepted(acceptedOrg.Id, currentUserId, MembershipRole.Admin));
        db.OrganizationMemberships.Add(OrganizationMembership.Request(requestedOrg.Id, currentUserId, MembershipRole.Member));
        // Invited by email only -- no UserId linked yet, matched via the current user's email.
        db.OrganizationMemberships.Add(OrganizationMembership.Invite(
            invitedOrg.Id, userId: null, currentUser.Email, MembershipRole.Member, Guid.NewGuid()));
        await db.SaveChangesAsync();

        var handler = new MyOrganizationsQueryHandler(db, new FakeCurrentUserService(currentUserId));
        var result = await handler.Handle(new MyOrganizationsQuery(), CancellationToken.None);

        Assert.Single(result.Organizations);
        Assert.Equal(acceptedOrg.Id, result.Organizations[0].OrganizationId);

        Assert.Single(result.Requests);
        Assert.Equal(requestedOrg.Id, result.Requests[0].OrganizationId);

        Assert.Single(result.Invitations);
        Assert.Equal(invitedOrg.Id, result.Invitations[0].OrganizationId);
    }
}
