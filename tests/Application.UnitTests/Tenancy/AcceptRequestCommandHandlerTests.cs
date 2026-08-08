using ErpApp.Application.Common.Exceptions;
using ErpApp.Application.Tenancy.Commands.AcceptRequest;
using ErpApp.Application.UnitTests.TestSupport;
using ErpApp.Domain.Tenancy;

namespace ErpApp.Application.UnitTests.Tenancy;

public class AcceptRequestCommandHandlerTests
{
    [Fact]
    public async Task Handle_accepts_request_when_actor_is_an_org_admin()
    {
        var db = TestAppDbContext.Create();
        var adminId = Guid.NewGuid();
        var organization = Organization.Create(
            "Acme Traders", "Retail", null, new DateOnly(2026, 1, 1), true,
            "acme-traders", null, null, null, null, adminId);
        db.Organizations.Add(organization);
        db.OrganizationMemberships.Add(OrganizationMembership.CreateAccepted(organization.Id, adminId, MembershipRole.Admin));
        var request = OrganizationMembership.Request(organization.Id, Guid.NewGuid(), MembershipRole.Member);
        db.OrganizationMemberships.Add(request);
        await db.SaveChangesAsync();

        var handler = new AcceptRequestCommandHandler(db, new FakeCurrentUserService(adminId));
        await handler.Handle(new AcceptRequestCommand(request.Id), CancellationToken.None);

        Assert.Equal(MembershipStatus.Accepted, request.Status);
    }

    [Fact]
    public async Task Handle_throws_forbidden_when_actor_is_not_an_org_admin()
    {
        var db = TestAppDbContext.Create();
        var adminId = Guid.NewGuid();
        var organization = Organization.Create(
            "Acme Traders", "Retail", null, new DateOnly(2026, 1, 1), true,
            "acme-traders", null, null, null, null, adminId);
        db.Organizations.Add(organization);
        db.OrganizationMemberships.Add(OrganizationMembership.CreateAccepted(organization.Id, adminId, MembershipRole.Admin));
        var request = OrganizationMembership.Request(organization.Id, Guid.NewGuid(), MembershipRole.Member);
        db.OrganizationMemberships.Add(request);
        await db.SaveChangesAsync();

        var handler = new AcceptRequestCommandHandler(db, new FakeCurrentUserService(Guid.NewGuid()));

        await Assert.ThrowsAsync<ForbiddenException>(() => handler.Handle(
            new AcceptRequestCommand(request.Id), CancellationToken.None));
    }

    [Fact]
    public async Task Handle_throws_conflict_when_request_is_no_longer_pending()
    {
        var db = TestAppDbContext.Create();
        var adminId = Guid.NewGuid();
        var organization = Organization.Create(
            "Acme Traders", "Retail", null, new DateOnly(2026, 1, 1), true,
            "acme-traders", null, null, null, null, adminId);
        db.Organizations.Add(organization);
        db.OrganizationMemberships.Add(OrganizationMembership.CreateAccepted(organization.Id, adminId, MembershipRole.Admin));
        var request = OrganizationMembership.Request(organization.Id, Guid.NewGuid(), MembershipRole.Member);
        request.Accept();
        db.OrganizationMemberships.Add(request);
        await db.SaveChangesAsync();

        var handler = new AcceptRequestCommandHandler(db, new FakeCurrentUserService(adminId));

        await Assert.ThrowsAsync<ConflictException>(() => handler.Handle(
            new AcceptRequestCommand(request.Id), CancellationToken.None));
    }
}
