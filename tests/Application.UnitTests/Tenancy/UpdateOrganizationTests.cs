using ErpApp.Application.Common.Exceptions;
using ErpApp.Application.Common.Persistence;
using ErpApp.Application.Common.Security;
using ErpApp.Application.Tenancy.Commands.CreateOrganization;
using ErpApp.Application.Tenancy.Commands.UpdateOrganization;
using ErpApp.Application.Tenancy.Queries.GetOrganizationProfile;
using ErpApp.Application.UnitTests.TestSupport;
using ErpApp.Domain.Catalog;
using ErpApp.Domain.Inventory;
using ErpApp.Domain.Tenancy;

namespace ErpApp.Application.UnitTests.Tenancy;

/// <summary>
/// Phase 43 (39 carried item #2) -- <c>UpdateOrganizationCommand</c>, the write path for the details
/// phase 39 could only display.
///
/// <para>The assertions worth having here are not "it sets the fields" (the Domain tests cover that)
/// but the three things a command can get wrong that no Domain test can see: that the write is
/// visible through the <i>read</i> path the screen actually uses, that the one field this command
/// refuses to touch stays untouched through it, and that the accounting-start-date guard fires on
/// the state that makes it necessary rather than on every edit.</para>
/// </summary>
public class UpdateOrganizationTests
{
    [Fact]
    public async Task The_update_is_visible_through_the_query_the_screen_reads()
    {
        var db = TestAppDbContext.Create();
        var organizationId = await SeedOrganizationAsync(db);

        await new UpdateOrganizationCommandHandler(db).Handle(
            new UpdateOrganizationCommand(
                organizationId, "Acme Traders Pvt Ltd", "Wholesale", "Naxal, Kathmandu",
                new DateOnly(2026, 4, 14), false,
                "billing@acme.example", "01-4444444", "301234567", "acme.example"),
            CancellationToken.None);

        // Phase-31's rule in test form: a field is reachable only if the command that writes it and
        // the read path the screen calls agree. Asserting on the aggregate would prove neither half.
        var profile = await new GetOrganizationProfileQueryHandler(db).Handle(
            new GetOrganizationProfileQuery(organizationId), CancellationToken.None);

        Assert.Equal("Acme Traders Pvt Ltd", profile.Name);
        Assert.Equal("Wholesale", profile.Industry);
        Assert.Equal("Naxal, Kathmandu", profile.Address);
        Assert.Equal(new DateOnly(2026, 4, 14), profile.AccountingStartDate);
        Assert.False(profile.IsVatRegistered);
        Assert.Equal("billing@acme.example", profile.Email);
        Assert.Equal("01-4444444", profile.Phone);
        Assert.Equal("301234567", profile.PanNumber);
        Assert.Equal("acme.example", profile.Website);
    }

    [Fact]
    public async Task The_workspace_name_survives_an_update()
    {
        var db = TestAppDbContext.Create();
        var organizationId = await SeedOrganizationAsync(db);

        await new UpdateOrganizationCommandHandler(db).Handle(
            new UpdateOrganizationCommand(
                organizationId, "Renamed", "Wholesale", null, new DateOnly(2026, 1, 1), true,
                null, null, null, null),
            CancellationToken.None);

        var profile = await new GetOrganizationProfileQueryHandler(db).Handle(
            new GetOrganizationProfileQuery(organizationId), CancellationToken.None);

        Assert.Equal("acme-traders", profile.WorkspaceName);
    }

    [Fact]
    public async Task An_unknown_organization_is_not_found()
    {
        var db = TestAppDbContext.Create();

        await Assert.ThrowsAsync<NotFoundException>(() =>
            new UpdateOrganizationCommandHandler(db).Handle(
                new UpdateOrganizationCommand(
                    Guid.NewGuid(), "Acme", "Retail", null, new DateOnly(2026, 1, 1), true,
                    null, null, null, null),
                CancellationToken.None));
    }

    /// <summary>
    /// The guard, and the state that makes it necessary. An opening-stock FIFO layer is stamped with
    /// whatever the accounting start date was when it was written and nothing ever restates it, so
    /// moving the date afterwards leaves the layers dated to the old day zero -- phase-37's rule that
    /// a stock value has to reach all three views, arriving through the date instead of the amount.
    /// </summary>
    [Fact]
    public async Task The_accounting_start_date_cannot_move_once_opening_stock_exists()
    {
        var db = TestAppDbContext.Create();
        var organizationId = await SeedOrganizationAsync(db);
        await SeedOpeningStockAsync(db, organizationId);

        var error = await Assert.ThrowsAsync<ConflictException>(() =>
            new UpdateOrganizationCommandHandler(db).Handle(
                new UpdateOrganizationCommand(
                    organizationId, "Acme Traders", "Retail", null, new DateOnly(2026, 4, 14), true,
                    null, null, null, null),
                CancellationToken.None));

        Assert.Contains("opening stock", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// And the half that would be easy to get wrong: with opening stock present, every OTHER detail
    /// must still be editable. A guard that refuses the whole command would make the screen useless
    /// for the tenants most likely to need it.
    /// </summary>
    [Fact]
    public async Task Every_other_detail_is_still_editable_once_opening_stock_exists()
    {
        var db = TestAppDbContext.Create();
        var organizationId = await SeedOrganizationAsync(db);
        await SeedOpeningStockAsync(db, organizationId);

        await new UpdateOrganizationCommandHandler(db).Handle(
            new UpdateOrganizationCommand(
                organizationId, "Acme Traders Pvt Ltd", "Wholesale", "Naxal",
                // The same date it already has, which is what an ordinary edit sends back.
                new DateOnly(2026, 1, 1), true, null, null, "301234567", null),
            CancellationToken.None);

        var profile = await new GetOrganizationProfileQueryHandler(db).Handle(
            new GetOrganizationProfileQuery(organizationId), CancellationToken.None);

        Assert.Equal("Acme Traders Pvt Ltd", profile.Name);
        Assert.Equal("301234567", profile.PanNumber);
    }

    [Fact]
    public void The_command_reuses_phase_39s_admin_only_profile_key()
    {
        // Not a new key: the logo write on the same screen already uses this one, and two keys on one
        // form would be two ways to be denied on it.
        var command = new UpdateOrganizationCommand(
            Guid.NewGuid(), "Acme", "Retail", null, new DateOnly(2026, 1, 1), true, null, null, null, null);

        Assert.Equal(PermissionKeys.OrganizationProfileManage, command.PermissionKey);
    }

    private static async Task<Guid> SeedOrganizationAsync(IAppDbContext db)
    {
        var organization = Organization.Create(
            "Acme Traders", "Retail", null, new DateOnly(2026, 1, 1), true,
            "acme-traders", null, null, null, null, Guid.NewGuid());

        db.Organizations.Add(organization);
        await db.SaveChangesAsync(CancellationToken.None);

        return organization.Id;
    }

    private static async Task SeedOpeningStockAsync(IAppDbContext db, Guid organizationId)
    {
        db.OpeningStockLines.Add(OpeningStockLine.Create(
            organizationId, Guid.NewGuid(), Guid.NewGuid(), 10m, 100m));
        await db.SaveChangesAsync(CancellationToken.None);
    }
}
