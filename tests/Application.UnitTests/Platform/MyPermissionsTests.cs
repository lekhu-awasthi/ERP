using ErpApp.Application.Common.Persistence;
using ErpApp.Application.Common.Security;
using ErpApp.Application.Platform.Queries.GetMyPermissions;
using ErpApp.Application.UnitTests.TestSupport;
using ErpApp.Domain.Tenancy;

namespace ErpApp.Application.UnitTests.Platform;

/// <summary>
/// The read behind the shell's permission-aware nav. It must answer exactly what
/// <c>AuthorizationBehavior</c> would: Accepted memberships, granted rows only, the caller only, this
/// organization only, and location-scoped grants reported apart from organization-wide ones.
/// </summary>
public class MyPermissionsTests
{
    private static readonly Guid UserId = Guid.NewGuid();

    [Fact]
    public async Task Returns_the_callers_organization_wide_keys_in_order()
    {
        var db = TestAppDbContext.Create();
        var organizationId = Guid.NewGuid();
        await PermissionGrantSeed.GrantAsync(
            db, organizationId, UserId, PermissionKeys.InvoiceView, PermissionKeys.ContactView);

        var result = await GetAsync(db, organizationId);

        Assert.Equal([PermissionKeys.ContactView, PermissionKeys.InvoiceView], result.OrganizationWide);
        Assert.Empty(result.AtSomeLocation);
    }

    [Fact]
    public async Task A_denied_row_is_not_a_grant()
    {
        var db = TestAppDbContext.Create();
        var organizationId = Guid.NewGuid();
        db.OrganizationMemberships.Add(
            OrganizationMembership.CreateAccepted(organizationId, UserId, MembershipRole.Admin));
        db.RolePermissions.Add(RolePermission.Create(Guid.NewGuid(), Role.AdminId, PermissionKeys.InvoiceView, false));
        await db.SaveChangesAsync(CancellationToken.None);

        var result = await GetAsync(db, organizationId);

        Assert.Empty(result.OrganizationWide);
        Assert.Empty(result.AtSomeLocation);
    }

    /// <summary>
    /// Phase 32b: a key held only at one branch is reported, but apart, so the shell can offer the
    /// screen (the list narrows itself to that branch) without the answer reading as tenant-wide.
    /// </summary>
    [Fact]
    public async Task A_location_scoped_grant_is_reported_apart_and_only_once()
    {
        var db = TestAppDbContext.Create();
        var organizationId = Guid.NewGuid();
        db.OrganizationMemberships.Add(
            OrganizationMembership.CreateAccepted(organizationId, UserId, MembershipRole.Admin));
        db.RolePermissions.Add(RolePermission.Create(Guid.NewGuid(), Role.AdminId, PermissionKeys.ContactView, true));
        db.RolePermissions.Add(RolePermission.Create(
            Guid.NewGuid(), Role.AdminId, PermissionKeys.InvoiceView, true, locationId: Guid.NewGuid()));
        db.RolePermissions.Add(RolePermission.Create(
            Guid.NewGuid(), Role.AdminId, PermissionKeys.InvoiceView, true, locationId: Guid.NewGuid()));
        await db.SaveChangesAsync(CancellationToken.None);

        var result = await GetAsync(db, organizationId);

        Assert.Equal([PermissionKeys.ContactView], result.OrganizationWide);
        Assert.Equal([PermissionKeys.InvoiceView], result.AtSomeLocation);
    }

    [Fact]
    public async Task A_key_held_organization_wide_is_not_repeated_as_location_scoped()
    {
        var db = TestAppDbContext.Create();
        var organizationId = Guid.NewGuid();
        db.OrganizationMemberships.Add(
            OrganizationMembership.CreateAccepted(organizationId, UserId, MembershipRole.Admin));
        db.RolePermissions.Add(RolePermission.Create(Guid.NewGuid(), Role.AdminId, PermissionKeys.InvoiceView, true));
        db.RolePermissions.Add(RolePermission.Create(
            Guid.NewGuid(), Role.AdminId, PermissionKeys.InvoiceView, true, locationId: Guid.NewGuid()));
        await db.SaveChangesAsync(CancellationToken.None);

        var result = await GetAsync(db, organizationId);

        Assert.Equal([PermissionKeys.InvoiceView], result.OrganizationWide);
        Assert.Empty(result.AtSomeLocation);
    }

    [Fact]
    public async Task Another_organization_and_a_pending_membership_grant_nothing()
    {
        var db = TestAppDbContext.Create();
        var mine = Guid.NewGuid();
        var pending = Guid.NewGuid();
        await PermissionGrantSeed.GrantAsync(db, mine, UserId, PermissionKeys.InvoiceView);
        db.OrganizationMemberships.Add(OrganizationMembership.Request(pending, UserId, MembershipRole.Admin));
        await db.SaveChangesAsync(CancellationToken.None);

        Assert.Empty((await GetAsync(db, Guid.NewGuid())).OrganizationWide);
        Assert.Empty((await GetAsync(db, pending)).OrganizationWide);
    }

    [Fact]
    public async Task Another_users_grants_are_not_the_callers()
    {
        var db = TestAppDbContext.Create();
        var organizationId = Guid.NewGuid();
        await PermissionGrantSeed.GrantAsync(db, organizationId, Guid.NewGuid(), PermissionKeys.InvoiceView);

        Assert.Empty((await GetAsync(db, organizationId)).OrganizationWide);
    }

    [Fact]
    public void The_read_is_gated_on_the_key_the_shell_already_needs()
    {
        var query = new GetMyPermissionsQuery(Guid.NewGuid());

        Assert.Equal(PermissionKeys.SubscriptionView, query.PermissionKey);
    }

    private static Task<MyPermissionsDto> GetAsync(IAppDbContext db, Guid organizationId) =>
        new GetMyPermissionsQueryHandler(db, new FakeCurrentUserService(UserId))
            .Handle(new GetMyPermissionsQuery(organizationId), CancellationToken.None);
}
