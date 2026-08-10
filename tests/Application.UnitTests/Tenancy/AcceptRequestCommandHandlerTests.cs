using ErpApp.Application.Common.Exceptions;
using ErpApp.Application.Tenancy.Commands.AcceptRequest;
using ErpApp.Application.UnitTests.TestSupport;
using ErpApp.Domain.Tenancy;

namespace ErpApp.Application.UnitTests.Tenancy;

/// <summary>
/// The "is this user an org admin" check moved to AuthorizationBehavior (Phase 1c) -- see
/// AuthorizationBehaviorTests for that coverage. These tests exercise only what the handler
/// itself still owns: applying Accept() to the target membership.
/// </summary>
public class AcceptRequestCommandHandlerTests
{
    [Fact]
    public async Task Handle_accepts_a_pending_request()
    {
        var db = TestAppDbContext.Create();
        var organization = Organization.Create(
            "Acme Traders", "Retail", null, new DateOnly(2026, 1, 1), true,
            "acme-traders", null, null, null, null, Guid.NewGuid());
        db.Organizations.Add(organization);
        var request = OrganizationMembership.Request(organization.Id, Guid.NewGuid(), MembershipRole.Member);
        db.OrganizationMemberships.Add(request);
        await db.SaveChangesAsync();

        var handler = new AcceptRequestCommandHandler(db);
        await handler.Handle(new AcceptRequestCommand(request.Id), CancellationToken.None);

        Assert.Equal(MembershipStatus.Accepted, request.Status);
    }

    [Fact]
    public async Task Handle_throws_not_found_for_unknown_membership()
    {
        var db = TestAppDbContext.Create();
        var handler = new AcceptRequestCommandHandler(db);

        await Assert.ThrowsAsync<NotFoundException>(() => handler.Handle(
            new AcceptRequestCommand(Guid.NewGuid()), CancellationToken.None));
    }

    [Fact]
    public async Task Handle_throws_conflict_when_request_is_no_longer_pending()
    {
        var db = TestAppDbContext.Create();
        var organization = Organization.Create(
            "Acme Traders", "Retail", null, new DateOnly(2026, 1, 1), true,
            "acme-traders", null, null, null, null, Guid.NewGuid());
        db.Organizations.Add(organization);
        var request = OrganizationMembership.Request(organization.Id, Guid.NewGuid(), MembershipRole.Member);
        request.Accept();
        db.OrganizationMemberships.Add(request);
        await db.SaveChangesAsync();

        var handler = new AcceptRequestCommandHandler(db);

        await Assert.ThrowsAsync<ConflictException>(() => handler.Handle(
            new AcceptRequestCommand(request.Id), CancellationToken.None));
    }
}
