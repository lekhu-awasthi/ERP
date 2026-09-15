using ErpApp.Application.Common.Exceptions;
using ErpApp.Application.Common.Persistence;
using ErpApp.Application.Common.Security;
using ErpApp.Application.Tenancy.Commands.BackfillDocumentLocations;
using ErpApp.Application.UnitTests.TestSupport;
using ErpApp.Domain.Common;
using ErpApp.Domain.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.UnitTests.Locations;

/// <summary>
/// Phase 44 (35a carried item #5, 35b #6) -- the one-off billing-location backfill.
///
/// <para>The state it exists for is real and not reachable through the write path: a document
/// raised before the tenant had a HeadOffice, or while its type was outside
/// <c>LocationScopeMode</c>, carries no location. Every location-filtered report then omits it
/// while the unfiltered one still counts it.</para>
/// </summary>
public class BackfillDocumentLocationsCommandHandlerTests
{
    [Fact]
    public void The_command_is_admin_only_and_organization_scoped()
    {
        var command = new BackfillDocumentLocationsCommand(Guid.NewGuid());

        Assert.Equal(PermissionKeys.OrganizationBackfillLocations, command.PermissionKey);
        Assert.Equal("Tenancy.Organization.BackfillLocations", command.PermissionKey);

        // Every IOrganizationScoped request must also be IRequirePermission -- AuthorizationBehavior
        // is the only thing verifying org membership at all (CLAUDE.md).
        Assert.IsAssignableFrom<IOrganizationScoped>(command);
        Assert.IsAssignableFrom<IRequirePermission>(command);
    }

    [Fact]
    public async Task It_assigns_head_office_to_documents_that_carry_no_location_and_reports_the_count()
    {
        var db = TestAppDbContext.Create();
        var seed = await TradeReportSeed.CreateAsync(db);
        var date = new DateOnly(2026, 1, 10);

        // Two invoices raised before the tenant had any location at all.
        await seed.ApproveInvoiceAsync(db, date, 1_000m);
        await seed.ApproveInvoiceAsync(db, date, 250m);

        var headOffice = await SeedHeadOfficeAsync(db, seed.OrganizationId);

        // A third, raised afterwards, already has one -- and must not be counted as backfilled.
        await seed.ApproveInvoiceAsync(db, date, 400m, locationId: headOffice);

        var result = await new BackfillDocumentLocationsCommandHandler(db).Handle(
            new BackfillDocumentLocationsCommand(seed.OrganizationId), CancellationToken.None);

        Assert.Equal(headOffice, result.HeadOfficeId);
        Assert.Equal(2, result.TotalUpdated);

        var invoices = Assert.Single(result.Counts, x => x.DocumentType == DocumentType.Invoice);
        Assert.Equal(2, invoices.Updated);

        // Nothing is left unplaced afterwards.
        Assert.Empty(await db.Invoices
            .Where(x => x.OrganizationId == seed.OrganizationId && x.LocationId == null)
            .ToListAsync(CancellationToken.None));
    }

    /// <summary>
    /// Idempotent by construction: it only ever writes rows whose location is null. That is what
    /// makes a dry-run mode unnecessary rather than merely unbuilt, and what makes a retry after a
    /// dropped response safe.
    /// </summary>
    [Fact]
    public async Task Running_it_twice_writes_nothing_the_second_time()
    {
        var db = TestAppDbContext.Create();
        var seed = await TradeReportSeed.CreateAsync(db);

        await seed.ApproveInvoiceAsync(db, new DateOnly(2026, 1, 10), 1_000m);
        await SeedHeadOfficeAsync(db, seed.OrganizationId);

        var handler = new BackfillDocumentLocationsCommandHandler(db);
        var first = await handler.Handle(
            new BackfillDocumentLocationsCommand(seed.OrganizationId), CancellationToken.None);
        var second = await handler.Handle(
            new BackfillDocumentLocationsCommand(seed.OrganizationId), CancellationToken.None);

        Assert.Equal(1, first.TotalUpdated);
        Assert.Equal(0, second.TotalUpdated);
        Assert.Empty(second.Counts);
    }

    /// <summary>
    /// The tenant's scope decides which types are backfilled. Under the default
    /// <c>SalesTransactionsOnly</c> a Purchase Bill deliberately carries no location, so writing one
    /// would make the setting a lie in the opposite direction.
    /// </summary>
    [Fact]
    public async Task It_leaves_a_type_that_is_out_of_scope_alone()
    {
        var db = TestAppDbContext.Create();
        var seed = await TradeReportSeed.CreateAsync(db);

        await seed.ApprovePurchaseBillAsync(db, new DateOnly(2026, 1, 10), 500m);
        await seed.ApproveInvoiceAsync(db, new DateOnly(2026, 1, 11), 1_000m);
        await SeedHeadOfficeAsync(db, seed.OrganizationId);

        // The default mode, stated rather than assumed.
        var settings = await db.TenantSettings.SingleAsync(
            x => x.OrganizationId == seed.OrganizationId, CancellationToken.None);
        Assert.Equal(LocationScopeMode.SalesTransactionsOnly, settings.LocationScopeMode);

        var result = await new BackfillDocumentLocationsCommandHandler(db).Handle(
            new BackfillDocumentLocationsCommand(seed.OrganizationId), CancellationToken.None);

        Assert.DoesNotContain(result.Counts, x => x.DocumentType == DocumentType.PurchaseBill);
        Assert.Single(result.Counts, x => x.DocumentType == DocumentType.Invoice);

        // ...and widening the scope then picks the bill up, which is the whole reason the command
        // exists: the tenant changed its mind after trading.
        settings.SetLocationSettings(LocationScopeMode.AllTransactions, locationWiseReportPermission: false);
        await db.SaveChangesAsync(CancellationToken.None);

        var widened = await new BackfillDocumentLocationsCommandHandler(db).Handle(
            new BackfillDocumentLocationsCommand(seed.OrganizationId), CancellationToken.None);

        Assert.Single(widened.Counts, x => x.DocumentType == DocumentType.PurchaseBill);
    }

    /// <summary>A tenant with no HeadOffice has nothing to assign, and says so rather than writing
    /// nulls over nulls and reporting success.</summary>
    [Fact]
    public async Task A_tenant_with_no_head_office_is_a_conflict()
    {
        var db = TestAppDbContext.Create();
        var seed = await TradeReportSeed.CreateAsync(db);
        await seed.ApproveInvoiceAsync(db, new DateOnly(2026, 1, 10), 1_000m);

        await Assert.ThrowsAsync<ConflictException>(() =>
            new BackfillDocumentLocationsCommandHandler(db).Handle(
                new BackfillDocumentLocationsCommand(seed.OrganizationId), CancellationToken.None));
    }

    /// <summary>
    /// It fills a null and refuses to move a location that is already set -- the rule
    /// <c>BackfillLocation</c> states, asserted on a document that is <b>Approved</b>, which is the
    /// whole population this command exists for and the one <c>SetLocation</c> refuses.
    /// </summary>
    [Fact]
    public async Task It_never_moves_a_location_that_is_already_set()
    {
        var db = TestAppDbContext.Create();
        var seed = await TradeReportSeed.CreateAsync(db);
        var headOffice = await SeedHeadOfficeAsync(db, seed.OrganizationId);

        var branch = BillingLocation.Create(seed.OrganizationId, "BR1", "Branch One", null, null);
        db.BillingLocations.Add(branch);
        await db.SaveChangesAsync(CancellationToken.None);

        var (invoiceId, _) = await seed.ApproveInvoiceAsync(
            db, new DateOnly(2026, 1, 10), 1_000m, locationId: branch.Id);

        var result = await new BackfillDocumentLocationsCommandHandler(db).Handle(
            new BackfillDocumentLocationsCommand(seed.OrganizationId), CancellationToken.None);

        Assert.Equal(0, result.TotalUpdated);

        var invoice = await db.Invoices.SingleAsync(x => x.Id == invoiceId, CancellationToken.None);
        Assert.Equal(branch.Id, invoice.LocationId);
        Assert.NotEqual(headOffice, invoice.LocationId);

        // And the aggregate says so itself rather than silently ignoring the call.
        Assert.Throws<InvalidOperationException>(() => invoice.BackfillLocation(headOffice));
    }

    /// <summary>
    /// The sweep guard. A backfill that covers sixteen of seventeen types is the phase-35a defect in
    /// a new place -- nothing fails, because nothing asks. <c>DocumentLocationScope</c> derives the
    /// universe from <c>DocumentMechanisms</c>, so adding a location-bearing <c>DocumentType</c>
    /// without teaching this command about it fails here rather than surfacing as a report that
    /// quietly omits rows.
    ///
    /// <para>A source scan rather than reflection, deliberately: "does this handler load that
    /// DbSet" is not a question reflection can answer, and the alternative -- one behavioural test
    /// per type -- would be seventeen fixtures asserting one line each.</para>
    /// </summary>
    [Fact]
    public void Every_location_bearing_document_type_is_covered_by_the_backfill()
    {
        var source = File.ReadAllText(HandlerSourcePath());

        // Phase-34a's rule: assert the input is non-empty, or every assertion over it is vacuous.
        Assert.False(string.IsNullOrWhiteSpace(source), "The handler source could not be read.");

        var missing = DocumentLocationScope.AllLocationBearingTypes
            .Where(type => !source.Contains($"DocumentType.{type}", StringComparison.Ordinal))
            .Select(type => type.ToString())
            .ToList();

        Assert.True(
            missing.Count == 0,
            "These location-bearing document types are not backfilled, so a tenant widening its "
            + "LocationScopeMode would leave them invisible to every filtered report:\n  "
            + string.Join("\n  ", missing));
    }

    private static string HandlerSourcePath()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "ErpApp.slnx")))
        {
            directory = directory.Parent;
        }

        Assert.NotNull(directory);
        return Path.Combine(
            directory!.FullName,
            "src", "Application", "Tenancy", "Commands", "BackfillDocumentLocations",
            "BackfillDocumentLocationsCommandHandler.cs");
    }

    private static async Task<Guid> SeedHeadOfficeAsync(IAppDbContext db, Guid organizationId)
    {
        var headOffice = BillingLocation.CreateHeadOffice(organizationId);
        db.BillingLocations.Add(headOffice);
        await db.SaveChangesAsync(CancellationToken.None);
        return headOffice.Id;
    }
}
