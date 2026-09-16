using ErpApp.Application.Common.Behaviors;
using ErpApp.Application.Common.Exceptions;
using ErpApp.Application.Common.Persistence;
using ErpApp.Application.Sales;
using ErpApp.Application.Sales.Commands.CreateInvoice;
using ErpApp.Application.Tenancy.Queries.MyOrganizations;
using ErpApp.Application.UnitTests.TestSupport;
using ErpApp.Domain.Catalog;
using ErpApp.Domain.Identity;
using ErpApp.Domain.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.UnitTests.Tenancy;

/// <summary>
/// Phase 49 -- what this product does when a tenant's term ends, pinned on both surfaces at once.
///
/// <para><b>The decision these tests encode.</b> Read live on 2026-09-16, the reference product
/// answers the equivalent of <see cref="MyOrganizationsQuery"/> for an expired trial with an empty
/// list: the organization is gone from the owner's namespaces and the portal shows first-run
/// onboarding. This product deliberately does not do that (docs/phase-49-status.md Decision A) --
/// the row stays and carries <c>IsExpired</c> instead. Both halves of that sentence are asserted
/// here, so a later change to either is a deliberate one rather than a silent drift.</para>
///
/// <para><b>Both surfaces on one seed, deliberately.</b> The picker says "expired" and
/// <c>SubscriptionExpiryBehavior</c> refuses the write, and those are two readings of one fact.
/// Phase 26b's rule as phase 36 had to re-learn it: two call sites that each derive "the same"
/// figure do not agree, they coincide. Asserting them against one seeded tenant in one test is what
/// makes the picker's badge mean the thing the user will actually hit.</para>
/// </summary>
public class ExpiredTenantVisibilityTests
{
    [Fact]
    public async Task An_expired_organization_stays_in_the_owners_list_and_is_marked_read_only()
    {
        var db = TestAppDbContext.Create();
        var (userId, organizationId) = await SeedMembershipAsync(db, "Lapsed Traders");
        var subscription = await SeedSubscriptionAsync(db, organizationId);
        await ExpireAsync(db, subscription);

        var result = await new MyOrganizationsQueryHandler(db, new FakeCurrentUserService(userId), TimeProvider.System)
            .Handle(new MyOrganizationsQuery(), CancellationToken.None);

        // The divergence itself: present, not absent. The reference product returns total: 0 here.
        var row = Assert.Single(result.Organizations);
        Assert.Equal(organizationId, row.OrganizationId);
        Assert.True(row.IsExpired);
        Assert.Equal(subscription.TermEndsAt, row.TermEndsAt);

        // ...and the badge means what it says: the same tenant's document writes are refused.
        await Assert.ThrowsAsync<ConflictException>(() => new SubscriptionExpiryBehavior<CreateInvoiceCommand, CreateInvoiceResult>(db)
            .Handle(DraftCommand(organizationId), () => Task.FromResult(Result()), CancellationToken.None));
    }

    [Fact]
    public async Task A_live_organization_is_listed_with_its_term_end_and_no_mark()
    {
        var db = TestAppDbContext.Create();
        var (userId, organizationId) = await SeedMembershipAsync(db, "Acme Traders");
        var subscription = await SeedSubscriptionAsync(db, organizationId);

        var result = await new MyOrganizationsQueryHandler(db, new FakeCurrentUserService(userId), TimeProvider.System)
            .Handle(new MyOrganizationsQuery(), CancellationToken.None);

        var row = Assert.Single(result.Organizations);
        Assert.False(row.IsExpired);
        Assert.Equal(subscription.TermEndsAt, row.TermEndsAt);

        var expected = Result();
        Assert.Same(expected, await new SubscriptionExpiryBehavior<CreateInvoiceCommand, CreateInvoiceResult>(db)
            .Handle(DraftCommand(organizationId), () => Task.FromResult(expected), CancellationToken.None));
    }

    /// <summary>
    /// The one thing that would make the divergence indefensible is an expired tenant that cannot be
    /// read. Phase 31's exclusion list is what prevents it, and this asserts the consequence the
    /// picker's badge promises: "Expired -- read-only" has to leave the tenant readable.
    /// </summary>
    [Fact]
    public async Task An_expired_organization_is_still_readable()
    {
        var db = TestAppDbContext.Create();
        var (_, organizationId) = await SeedMembershipAsync(db, "Lapsed Traders");
        await ExpireAsync(db, await SeedSubscriptionAsync(db, organizationId));

        // A query carries none of the three markers, so the behavior hands it straight on. Asserted
        // through the behavior rather than by inspecting the query type, because "read-only means
        // readable" is a property of the gate, not of any one request.
        var query = new GetSomethingQuery(organizationId);
        var behavior = new SubscriptionExpiryBehavior<GetSomethingQuery, string>(db);

        Assert.Equal("read", await behavior.Handle(query, () => Task.FromResult("read"), CancellationToken.None));
    }

    /// <summary>A stand-in for any of the ~300 queries in the app: organization-scoped, and carrying
    /// none of the three write markers.</summary>
    private sealed record GetSomethingQuery(Guid OrganizationId) : ErpApp.Application.Common.Security.IOrganizationScoped;

    private static CreateInvoiceCommand DraftCommand(Guid organizationId) =>
        new(organizationId, Guid.NewGuid(), Guid.NewGuid(), new DateOnly(2026, 1, 10), null,
            [new InvoiceLineInput(Guid.NewGuid(), 1m, 10m, VatRate.NoVat)]);

    private static CreateInvoiceResult Result() =>
        new(Guid.NewGuid(), "X", Domain.Sales.InvoiceStatus.Draft);

    private static async Task<(Guid UserId, Guid OrganizationId)> SeedMembershipAsync(IAppDbContext db, string name)
    {
        var user = User.Register("Jane Doe", "jane@example.com", "9800000000", "hashed");
        db.Users.Add(user);

        var organization = Organization.Create(
            name, "Retail", null, new DateOnly(2026, 1, 1), true,
            name.ToLowerInvariant().Replace(' ', '-'), null, null, null, null, user.Id);
        db.Organizations.Add(organization);

        db.OrganizationMemberships.Add(
            OrganizationMembership.CreateAccepted(organization.Id, user.Id, MembershipRole.Admin));

        // TestAppDbContext applies no configurations/HasData, so the well-known role rows the query
        // joins need seeding here (see its doc comment).
        db.Roles.Add(Role.Create(Role.AdminId, "Admin"));
        db.Roles.Add(Role.Create(Role.MemberId, "Member"));
        await db.SaveChangesAsync(CancellationToken.None);

        return (user.Id, organization.Id);
    }

    private static async Task<TenantSubscription> SeedSubscriptionAsync(IAppDbContext db, Guid organizationId)
    {
        var subscription = TenantSubscription.CreateTrial(organizationId, default(AccountingFeatureSelections));
        db.TenantSubscriptions.Add(subscription);
        await db.SaveChangesAsync(CancellationToken.None);

        return subscription;
    }

    /// <summary>
    /// Through EF's change tracker, never by relaxing the aggregate: <c>SetPlan</c> refuses an end
    /// date at or before the start and <c>CreateTrial</c> starts now, so only the passage of time
    /// produces this state. Phase 31's rule, and the same helper shape
    /// <c>GeneralSettingsAndSubscriptionTests.ExpireAsync</c> uses.
    /// </summary>
    private static async Task ExpireAsync(IAppDbContext db, TenantSubscription subscription)
    {
        var entry = ((DbContext)db).Entry(subscription);
        entry.Property(nameof(TenantSubscription.OriginatedAt)).CurrentValue = DateTimeOffset.UtcNow.AddDays(-30);
        entry.Property(nameof(TenantSubscription.TermStartsAt)).CurrentValue = DateTimeOffset.UtcNow.AddDays(-30);
        entry.Property(nameof(TenantSubscription.TermEndsAt)).CurrentValue = DateTimeOffset.UtcNow.AddDays(-15);
        await db.SaveChangesAsync(CancellationToken.None);
    }
}
