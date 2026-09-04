using ErpApp.Application.Common.Exceptions;
using ErpApp.Application.Common.Persistence;
using ErpApp.Application.Configuration.Commands.DeleteLookup;
using ErpApp.Application.Tenancy.Commands.CreateCurrency;
using ErpApp.Application.Tenancy.Commands.UpdateCurrency;
using ErpApp.Application.Tenancy.Queries.ListCurrencyCatalog;
using ErpApp.Application.UnitTests.TestSupport;
using ErpApp.Domain.Common;
using ErpApp.Domain.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.UnitTests.Currencies;

/// <summary>
/// Phase 28. The currency list itself: the MultiCurrency cap (phase-20f Decision #4's shape, for
/// the second time), the base-currency protections, and the Add New Currency picker.
/// </summary>
public class CurrencyManagementTests
{
    [Fact]
    public async Task A_tenant_without_the_entitlement_is_capped_at_the_base_currency_it_was_seeded_with()
    {
        var db = TestAppDbContext.Create();
        var organizationId = await SeedAsync(db, multiCurrency: false);

        var ex = await Assert.ThrowsAsync<FeatureNotEnabledException>(() =>
            new CreateCurrencyCommandHandler(db).Handle(
                new CreateCurrencyCommand(organizationId, "USD"), CancellationToken.None));

        Assert.Contains("Multi-Currency", ex.Message, StringComparison.Ordinal);
        Assert.Contains("NPR only", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_tenant_with_the_entitlement_can_add_a_second_currency()
    {
        var db = TestAppDbContext.Create();
        var organizationId = await SeedAsync(db, multiCurrency: true);

        var result = await new CreateCurrencyCommandHandler(db).Handle(
            new CreateCurrencyCommand(organizationId, "usd"), CancellationToken.None);

        Assert.Equal("USD", result.Code);
        Assert.Equal("US Dollar", result.Name);
        Assert.Equal(2, await db.Currencies.CountAsync(x => x.OrganizationId == organizationId));
    }

    [Fact]
    public async Task An_organization_with_no_currency_row_yet_can_always_create_its_first()
    {
        // The cap is stated as a cap, not a block, so organizations that predate this phase's
        // backfill are not wedged -- the same property phase-20f's warehouse cap has.
        var db = TestAppDbContext.Create();
        var organizationId = Guid.NewGuid();
        db.TenantSubscriptions.Add(TenantSubscription.CreateTrial(
            organizationId, new AccountingFeatureSelections(false, false, false, false, false, false, false)));
        await db.SaveChangesAsync(CancellationToken.None);

        var result = await new CreateCurrencyCommandHandler(db).Handle(
            new CreateCurrencyCommand(organizationId, "NPR"), CancellationToken.None);

        Assert.Equal("NPR", result.Code);
    }

    [Fact]
    public async Task The_same_currency_cannot_be_activated_twice()
    {
        var db = TestAppDbContext.Create();
        var organizationId = await SeedAsync(db, multiCurrency: true);

        await Assert.ThrowsAsync<ConflictException>(() =>
            new CreateCurrencyCommandHandler(db).Handle(
                new CreateCurrencyCommand(organizationId, "NPR"), CancellationToken.None));
    }

    [Fact]
    public async Task The_base_currency_cannot_be_deactivated()
    {
        var db = TestAppDbContext.Create();
        var organizationId = await SeedAsync(db, multiCurrency: true);
        var npr = await db.Currencies.SingleAsync(x => x.OrganizationId == organizationId);

        var ex = await Assert.ThrowsAsync<ConflictException>(() =>
            new UpdateCurrencyCommandHandler(db).Handle(
                new UpdateCurrencyCommand(organizationId, npr.Id, "Nepalese Rupee", "Rs.", IsActive: false),
                CancellationToken.None));

        Assert.Contains("base currency", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_base_currency_cannot_be_deleted()
    {
        var db = TestAppDbContext.Create();
        var organizationId = await SeedAsync(db, multiCurrency: true);
        var npr = await db.Currencies.SingleAsync(x => x.OrganizationId == organizationId);

        var ex = await Assert.ThrowsAsync<ConflictException>(() =>
            new DeleteLookupCommandHandler<Currency>(db).Handle(
                new DeleteLookupCommand<Currency>(organizationId, npr.Id), CancellationToken.None));

        Assert.Contains("cannot be removed", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_non_base_currency_can_be_renamed_and_deleted()
    {
        var db = TestAppDbContext.Create();
        var organizationId = await SeedAsync(db, multiCurrency: true);
        var usd = await new CreateCurrencyCommandHandler(db).Handle(
            new CreateCurrencyCommand(organizationId, "USD"), CancellationToken.None);

        var updated = await new UpdateCurrencyCommandHandler(db).Handle(
            new UpdateCurrencyCommand(organizationId, usd.Id, "US Dollars", "US$", IsActive: false),
            CancellationToken.None);
        Assert.Equal("US Dollars", updated.Name);
        Assert.False(updated.IsActive);

        await new DeleteLookupCommandHandler<Currency>(db).Handle(
            new DeleteLookupCommand<Currency>(organizationId, usd.Id), CancellationToken.None);
        Assert.Equal(1, await db.Currencies.CountAsync(x => x.OrganizationId == organizationId));
    }

    [Fact]
    public async Task The_catalog_picker_flags_what_the_tenant_has_already_activated()
    {
        var db = TestAppDbContext.Create();
        var organizationId = await SeedAsync(db, multiCurrency: true);

        var catalog = await new ListCurrencyCatalogQueryHandler(db).Handle(
            new ListCurrencyCatalogQuery(organizationId), CancellationToken.None);

        Assert.Equal(CurrencyCatalog.All.Count, catalog.Count);
        Assert.True(catalog.Single(x => x.Code == "NPR").AlreadyActivated);
        Assert.False(catalog.Single(x => x.Code == "USD").AlreadyActivated);
    }

    private static async Task<Guid> SeedAsync(IAppDbContext db, bool multiCurrency)
    {
        var organizationId = Guid.NewGuid();

        db.TenantSubscriptions.Add(TenantSubscription.CreateTrial(
            organizationId,
            new AccountingFeatureSelections(false, false, false, multiCurrency, false, false, false)));
        db.Currencies.Add(Currency.CreateBase(organizationId));
        await db.SaveChangesAsync(CancellationToken.None);

        return organizationId;
    }
}
