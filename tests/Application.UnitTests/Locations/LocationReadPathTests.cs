using ErpApp.Application.Common.Pagination;
using ErpApp.Application.Common.Persistence;
using ErpApp.Application.Common.Security;
using ErpApp.Application.Sales;
using ErpApp.Application.Sales.Commands.CreateInvoice;
using ErpApp.Application.Sales.Queries.GetInvoice;
using ErpApp.Application.Sales.Queries.ListInvoices;
using ErpApp.Application.UnitTests.TestSupport;
using ErpApp.Domain.Catalog;
using ErpApp.Domain.Sales;
using ErpApp.Domain.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.UnitTests.Locations;

/// <summary>
/// Phase 35a -- what the read path actually <i>does</i> with a billing location.
///
/// <para><c>LocationReadPathSweepGuardTests</c> is a shape check: it proves the field is declared in
/// all three places, and would pass just as happily against a handler that ignored it. These are
/// the behavioural half — the filter narrows, it composes with phase-32b's permission scope rather
/// than replacing it, and a stored location survives the round trip a form makes.</para>
///
/// <para>Invoice is the one type used throughout, deliberately: it is the type phase 32 wired by
/// hand, so anything true of it here is what the swept fourteen were made to match.</para>
/// </summary>
public class LocationReadPathTests
{
    private static readonly DateOnly Day = new(2026, 5, 10);

    [Fact]
    public async Task The_filter_narrows_to_one_location_and_null_means_all()
    {
        var db = TestAppDbContext.Create();
        var world = await SeedAsync(db);

        var atHead = await CreateInvoiceAsync(db, world, world.HeadOfficeId);
        var atBranch = await CreateInvoiceAsync(db, world, world.BranchId);

        Assert.Equal(2, (await ListAsync(db, world, locationId: null)).TotalCount);
        Assert.Equal(atHead, Assert.Single((await ListAsync(db, world, world.HeadOfficeId)).Items).Id);
        Assert.Equal(atBranch, Assert.Single((await ListAsync(db, world, world.BranchId)).Items).Id);
    }

    /// <summary>
    /// The two location narrowings are independent and both apply: phase 32b's scope says which
    /// branches a caller <i>may</i> see, and phase 35a's filter says which one they <i>asked</i>
    /// for. A caller scoped to the branch who filters to HeadOffice must see nothing — not
    /// HeadOffice's rows (the filter overriding the scope, a permission hole) and not the branch's
    /// (the scope overriding the filter, a wrong answer that looks checked).
    /// </summary>
    [Fact]
    public async Task The_filter_composes_with_the_permission_scope_rather_than_replacing_it()
    {
        var db = TestAppDbContext.Create();
        var world = await SeedAsync(db);

        await CreateInvoiceAsync(db, world, world.HeadOfficeId);
        var atBranch = await CreateInvoiceAsync(db, world, world.BranchId);

        var clerk = Guid.NewGuid();
        await ScopeToAsync(db, world, clerk, world.BranchId);

        // Unfiltered: the scope alone, so only the branch's row.
        var unfiltered = await ListAsync(db, world, locationId: null, userId: clerk);
        Assert.Equal(atBranch, Assert.Single(unfiltered.Items).Id);

        // Their own branch, asked for explicitly: the same row, not an error.
        var theirOwn = await ListAsync(db, world, world.BranchId, clerk);
        Assert.Equal(atBranch, Assert.Single(theirOwn.Items).Id);

        // A branch they hold no key at: empty, and specifically not HeadOffice's invoice.
        var elsewhere = await ListAsync(db, world, world.HeadOfficeId, clerk);
        Assert.Empty(elsewhere.Items);
    }

    /// <summary>
    /// The defect this phase exists to fix, stated as a test. The write path stored a location and
    /// the detail query dropped it, so the form re-posted the picker's default over the stored
    /// branch on the very next save — and nothing failed.
    /// </summary>
    [Fact]
    public async Task A_stored_location_survives_the_round_trip_a_form_makes()
    {
        var db = TestAppDbContext.Create();
        var world = await SeedAsync(db);

        var id = await CreateInvoiceAsync(db, world, world.BranchId);

        var detail = await new GetInvoiceQueryHandler(db).Handle(
            new GetInvoiceQuery(world.OrganizationId, id), CancellationToken.None);

        Assert.Equal(world.BranchId, detail.LocationId);
    }

    /// <summary>
    /// A caller who supplies nothing gets the tenant's HeadOffice, which is what the header picker
    /// shows by default — so "the user did not touch the control" and "the user chose HeadOffice"
    /// store the same thing, and neither reads back as absent.
    /// </summary>
    [Fact]
    public async Task Supplying_no_location_stores_the_tenants_head_office()
    {
        var db = TestAppDbContext.Create();
        var world = await SeedAsync(db);

        var id = await CreateInvoiceAsync(db, world, locationId: null);

        var detail = await new GetInvoiceQueryHandler(db).Handle(
            new GetInvoiceQuery(world.OrganizationId, id), CancellationToken.None);

        Assert.Equal(world.HeadOfficeId, detail.LocationId);
    }

    private sealed record World(
        Guid OrganizationId, InventoryReportSeed.Seed Seed, Guid HeadOfficeId, Guid BranchId);

    /// <summary>
    /// The phase-26c seed (customer, warehouse, products, accounts) plus two locations and the
    /// AllTransactions scope. Seeding through the real handlers is this codebase's rule; the two
    /// location rows are added directly because their own command is phase 32's and is covered by
    /// <c>BillingLocationManagementTests</c>.
    /// </summary>
    private static async Task<World> SeedAsync(IAppDbContext db)
    {
        var seed = await InventoryReportSeed.CreateAsync(db);

        // The seed already wrote this tenant's settings row -- widen the existing one rather than
        // adding a second, which InMemory happily accepts and every `SingleOrDefault` over it then
        // throws on.
        var settings = await db.TenantSettings.SingleAsync(
            x => x.OrganizationId == seed.OrganizationId, CancellationToken.None);
        settings.SetLocationSettings(LocationScopeMode.AllTransactions, locationWiseReportPermission: false);

        var headOffice = BillingLocation.CreateHeadOffice(seed.OrganizationId);
        var branch = BillingLocation.Create(seed.OrganizationId, "BR1", "Branch One", "Pokhara", null);
        db.BillingLocations.AddRange(headOffice, branch);

        await db.SaveChangesAsync(CancellationToken.None);

        return new World(seed.OrganizationId, seed, headOffice.Id, branch.Id);
    }

    /// <summary>
    /// A Service line, not a Goods one: a Goods line consumes stock regardless of TrackInventory,
    /// so an unapproved-but-created Goods invoice needs opening stock this test has no use for
    /// (phase-30's gotcha). These invoices are never approved -- the list and the detail query are
    /// what is under test.
    /// </summary>
    private static async Task<Guid> CreateInvoiceAsync(IAppDbContext db, World world, Guid? locationId)
    {
        var created = await new CreateInvoiceCommandHandler(db).Handle(
            new CreateInvoiceCommand(
                world.OrganizationId, world.Seed.CustomerId, world.Seed.WarehouseId, Day, null,
                [new InvoiceLineInput(world.Seed.ProductId, 1m, 100m, VatRate.NoVat)])
            {
                LocationId = locationId,
            },
            CancellationToken.None);

        return created.Id;
    }

    private static Task<PagedResult<Invoice>> ListAsync(
        IAppDbContext db, World world, Guid? locationId, Guid? userId = null) =>
        new ListInvoicesQueryHandler(db, new FakeCurrentUserService(userId ?? Guid.NewGuid())).Handle(
            new ListInvoicesQuery(world.OrganizationId, null, 1, 50, locationId),
            CancellationToken.None);

    /// <summary>Grants <c>Sales.Invoice.View</c> at one location only — phase 32b's shape.</summary>
    private static async Task ScopeToAsync(IAppDbContext db, World world, Guid userId, Guid locationId)
    {
        db.OrganizationMemberships.Add(
            OrganizationMembership.CreateAccepted(world.OrganizationId, userId, MembershipRole.Admin));
        db.RolePermissions.Add(RolePermission.Create(
            Guid.NewGuid(), Role.AdminId, PermissionKeys.InvoiceView, true, locationId));
        await db.SaveChangesAsync(CancellationToken.None);
    }
}
