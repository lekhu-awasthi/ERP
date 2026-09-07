using ErpApp.Application.Common.Exceptions;
using ErpApp.Application.Common.Locations;
using ErpApp.Application.Common.Persistence;
using ErpApp.Application.Tenancy.Commands.CreateBillingLocation;
using ErpApp.Application.Tenancy.Commands.UpdateBillingLocation;
using ErpApp.Application.Tenancy.Commands.UpdateBillingLocationSettings;
using ErpApp.Application.Tenancy.Queries.GetBillingLocationSettings;
using ErpApp.Application.Tenancy.Queries.ListBillingLocations;
using ErpApp.Application.UnitTests.TestSupport;
using ErpApp.Domain.Common;
using ErpApp.Domain.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.UnitTests.Locations;

/// <summary>
/// Phase 32. The location list itself -- the MultipleLocations cap (phase-20f Decision #4's shape for
/// the third time, after warehouses in 20f and currencies in 28), the HeadOffice protections, and the
/// Advanced panel's two settings.
/// </summary>
public class BillingLocationManagementTests
{
    [Fact]
    public async Task A_tenant_without_the_entitlement_is_capped_at_the_head_office_it_was_seeded_with()
    {
        var db = TestAppDbContext.Create();
        var organizationId = await SeedAsync(db, multipleLocations: false);

        var ex = await Assert.ThrowsAsync<FeatureNotEnabledException>(() =>
            new CreateBillingLocationCommandHandler(db).Handle(
                new CreateBillingLocationCommand(organizationId, "BR1", "Branch One", "Pokhara"),
                CancellationToken.None));

        Assert.Contains("Multiple Locations", ex.Message, StringComparison.Ordinal);
        Assert.Contains("HeadOffice", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_tenant_with_the_entitlement_can_add_a_second_location()
    {
        var db = TestAppDbContext.Create();
        var organizationId = await SeedAsync(db, multipleLocations: true);

        var result = await new CreateBillingLocationCommandHandler(db).Handle(
            new CreateBillingLocationCommand(organizationId, " BR1 ", " Branch One ", " Pokhara "),
            CancellationToken.None);

        Assert.Equal("BR1", result.Code);
        Assert.Equal("Branch One", result.Name);
        Assert.Equal(2, await db.BillingLocations.CountAsync(x => x.OrganizationId == organizationId));

        var created = await db.BillingLocations.SingleAsync(x => x.Id == result.Id);
        Assert.Equal(BillingLocationType.Standard, created.LocationType);
        Assert.False(created.IsHeadOffice);
    }

    /// <summary>The cap is a cap, not a block: an organization predating this phase's backfill must
    /// still be able to acquire its first location. Same property as phase-20f's warehouse cap.</summary>
    [Fact]
    public async Task An_organization_with_no_location_row_yet_can_always_create_its_first()
    {
        var db = TestAppDbContext.Create();
        var organizationId = Guid.NewGuid();
        db.TenantSubscriptions.Add(TenantSubscription.CreateTrial(
            organizationId, new AccountingFeatureSelections(false, false, false, false, false, false, false)));
        await db.SaveChangesAsync(CancellationToken.None);

        var result = await new CreateBillingLocationCommandHandler(db).Handle(
            new CreateBillingLocationCommand(organizationId, "HO2", "Head Office", "Kathmandu"),
            CancellationToken.None);

        Assert.Equal("HO2", result.Code);
    }

    [Fact]
    public async Task The_same_code_cannot_be_used_twice()
    {
        var db = TestAppDbContext.Create();
        var organizationId = await SeedAsync(db, multipleLocations: true);

        await Assert.ThrowsAsync<ConflictException>(() =>
            new CreateBillingLocationCommandHandler(db).Handle(
                new CreateBillingLocationCommand(organizationId, BillingLocation.HeadOfficeCode, "Duplicate", "X"),
                CancellationToken.None));
    }

    [Fact]
    public async Task The_head_office_can_be_renamed_but_never_deactivated()
    {
        var db = TestAppDbContext.Create();
        var organizationId = await SeedAsync(db, multipleLocations: true);
        var headOffice = await db.BillingLocations.SingleAsync(x => x.OrganizationId == organizationId);

        await new UpdateBillingLocationCommandHandler(db).Handle(
            new UpdateBillingLocationCommand(
                organizationId, headOffice.Id, "HO", "Main Office", "Kathmandu", null, true),
            CancellationToken.None);

        Assert.Equal("Main Office", (await db.BillingLocations.SingleAsync(x => x.Id == headOffice.Id)).Name);

        var ex = await Assert.ThrowsAsync<ConflictException>(() =>
            new UpdateBillingLocationCommandHandler(db).Handle(
                new UpdateBillingLocationCommand(
                    organizationId, headOffice.Id, "HO", "Main Office", "Kathmandu", null, false),
                CancellationToken.None));

        Assert.Contains("HeadOffice", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_list_hides_inactive_locations_unless_asked()
    {
        var db = TestAppDbContext.Create();
        var organizationId = await SeedAsync(db, multipleLocations: true);

        var branch = await new CreateBillingLocationCommandHandler(db).Handle(
            new CreateBillingLocationCommand(organizationId, "BR1", "Branch One", "Pokhara"), CancellationToken.None);

        await new UpdateBillingLocationCommandHandler(db).Handle(
            new UpdateBillingLocationCommand(organizationId, branch.Id, "BR1", "Branch One", "Pokhara", null, false),
            CancellationToken.None);

        var active = await new ListBillingLocationsQueryHandler(db).Handle(
            new ListBillingLocationsQuery(organizationId), CancellationToken.None);
        var all = await new ListBillingLocationsQueryHandler(db).Handle(
            new ListBillingLocationsQuery(organizationId, IncludeInactive: true), CancellationToken.None);

        Assert.Single(active);
        Assert.Equal(2, all.Count);
        Assert.True(active[0].IsHeadOffice);
    }

    [Fact]
    public async Task The_advanced_panel_is_not_configurable_without_the_entitlement()
    {
        var db = TestAppDbContext.Create();
        var organizationId = await SeedAsync(db, multipleLocations: false);

        await Assert.ThrowsAsync<FeatureNotEnabledException>(() =>
            new UpdateBillingLocationSettingsCommandHandler(db).Handle(
                new UpdateBillingLocationSettingsCommand(organizationId, LocationScopeMode.AllTransactions, true),
                CancellationToken.None));
    }

    /// <summary>
    /// The setting's whole purpose: widening the scope changes which document types carry a location,
    /// and the server resolves that set rather than leaving the client to re-derive the rule.
    /// </summary>
    [Fact]
    public async Task Widening_the_scope_changes_the_document_types_that_carry_a_location()
    {
        var db = TestAppDbContext.Create();
        var organizationId = await SeedAsync(db, multipleLocations: true);

        var before = await new GetBillingLocationSettingsQueryHandler(db).Handle(
            new GetBillingLocationSettingsQuery(organizationId), CancellationToken.None);

        Assert.Equal(LocationScopeMode.SalesTransactionsOnly, before.LocationScopeMode);
        Assert.False(before.LocationWiseReportPermission);
        Assert.Contains(nameof(DocumentType.Invoice), before.LocationBearingDocumentTypes);
        Assert.DoesNotContain(nameof(DocumentType.PurchaseBill), before.LocationBearingDocumentTypes);

        var after = await new UpdateBillingLocationSettingsCommandHandler(db).Handle(
            new UpdateBillingLocationSettingsCommand(organizationId, LocationScopeMode.AllTransactions, true),
            CancellationToken.None);

        Assert.Equal(LocationScopeMode.AllTransactions, after.LocationScopeMode);
        Assert.True(after.LocationWiseReportPermission);
        Assert.Contains(nameof(DocumentType.PurchaseBill), after.LocationBearingDocumentTypes);
        Assert.Equal(DocumentMechanisms.LocationBearing.Count, after.LocationBearingDocumentTypes.Count);
    }

    // ---- LocationResolver: the four rules all 17 handlers share -------------------------------

    [Fact]
    public async Task An_out_of_scope_document_type_stores_no_location_even_if_one_is_supplied()
    {
        var db = TestAppDbContext.Create();
        var organizationId = await SeedAsync(db, multipleLocations: true);
        var headOffice = await db.BillingLocations.SingleAsync(x => x.OrganizationId == organizationId);

        // Default scope is SalesTransactionsOnly, which excludes PurchaseBill. A client that keeps
        // sending a location after an Admin narrows the scope must not quietly keep writing it.
        var resolved = await LocationResolver.ResolveAsync(
            db, organizationId, DocumentType.PurchaseBill, headOffice.Id, CancellationToken.None);

        Assert.Null(resolved);
    }

    [Fact]
    public async Task An_in_scope_document_type_defaults_to_head_office_when_none_is_supplied()
    {
        var db = TestAppDbContext.Create();
        var organizationId = await SeedAsync(db, multipleLocations: true);
        var headOffice = await db.BillingLocations.SingleAsync(x => x.OrganizationId == organizationId);

        var resolved = await LocationResolver.ResolveAsync(
            db, organizationId, DocumentType.Invoice, null, CancellationToken.None);

        Assert.Equal(headOffice.Id, resolved);
    }

    [Fact]
    public async Task A_location_from_another_organization_is_not_found()
    {
        var db = TestAppDbContext.Create();
        var organizationId = await SeedAsync(db, multipleLocations: true);
        var otherOrganizationId = await SeedAsync(db, multipleLocations: true);
        var foreign = await db.BillingLocations.SingleAsync(x => x.OrganizationId == otherOrganizationId);

        await Assert.ThrowsAsync<NotFoundException>(() => LocationResolver.ResolveAsync(
            db, organizationId, DocumentType.Invoice, foreign.Id, CancellationToken.None));
    }

    /// <summary>
    /// An inactive location is a branch the tenant has closed: historical documents keep pointing at
    /// it (which is why deactivation is allowed and deletion is not), but a new document must not be
    /// raised from it.
    /// </summary>
    [Fact]
    public async Task An_inactive_location_cannot_be_used_on_a_new_document()
    {
        var db = TestAppDbContext.Create();
        var organizationId = await SeedAsync(db, multipleLocations: true);

        var branch = await new CreateBillingLocationCommandHandler(db).Handle(
            new CreateBillingLocationCommand(organizationId, "BR1", "Branch One", "Pokhara"), CancellationToken.None);
        await new UpdateBillingLocationCommandHandler(db).Handle(
            new UpdateBillingLocationCommand(organizationId, branch.Id, "BR1", "Branch One", "Pokhara", null, false),
            CancellationToken.None);

        await Assert.ThrowsAsync<NotFoundException>(() => LocationResolver.ResolveAsync(
            db, organizationId, DocumentType.Invoice, branch.Id, CancellationToken.None));
    }

    /// <summary>
    /// A tenant with no HeadOffice row resolves to null rather than throwing. Seeding makes that
    /// unreachable for any organization this phase created or migrated, but failing a document save
    /// over a missing default would be the phase-20f failure mode -- a tenant unable to invoice at
    /// all because of a feature it never asked for.
    /// </summary>
    [Fact]
    public async Task A_tenant_with_no_head_office_row_resolves_to_null_rather_than_failing()
    {
        var db = TestAppDbContext.Create();
        var organizationId = Guid.NewGuid();
        db.TenantSettings.Add(TenantSettings.CreateDefault(organizationId));
        await db.SaveChangesAsync(CancellationToken.None);

        var resolved = await LocationResolver.ResolveAsync(
            db, organizationId, DocumentType.Invoice, null, CancellationToken.None);

        Assert.Null(resolved);
    }

    private static async Task<Guid> SeedAsync(IAppDbContext db, bool multipleLocations)
    {
        var organizationId = Guid.NewGuid();

        db.TenantSettings.Add(TenantSettings.CreateDefault(organizationId));
        db.TenantSubscriptions.Add(TenantSubscription.CreateTrial(
            organizationId,
            new AccountingFeatureSelections(false, multipleLocations, false, false, false, false, false)));
        db.BillingLocations.Add(BillingLocation.CreateHeadOffice(organizationId));

        await db.SaveChangesAsync(CancellationToken.None);
        return organizationId;
    }
}
