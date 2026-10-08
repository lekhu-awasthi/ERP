using ErpApp.Application.Common.Behaviors;
using ErpApp.Application.Common.Exceptions;
using ErpApp.Application.Common.Persistence;
using ErpApp.Application.Configuration.Commands.CreatePaymentMode;
using ErpApp.Application.Configuration.Commands.UpdatePaymentMode;
using ErpApp.Application.Contacts.Commands.DeactivateContact;
using ErpApp.Application.Pos.Commands.SetLocationPosMode;
using ErpApp.Application.Pos.Commands.SetPosLocationPaymentModes;
using ErpApp.Application.Pos.Commands.UpdatePosLocationSettings;
using ErpApp.Application.Pos.Queries.GetPosConfiguration;
using ErpApp.Application.Pos.Queries.GetPosLocationSettings;
using ErpApp.Application.Tenancy.Commands.CreateOrganization;
using ErpApp.Application.Tenancy.Commands.UpdateAccountingDefaults;
using ErpApp.Application.Tenancy.Queries.GetAccountingDefaults;
using ErpApp.Application.UnitTests.TestSupport;
using ErpApp.Domain.Accounting;
using ErpApp.Domain.Configuration;
using ErpApp.Domain.Contacts;
using ErpApp.Domain.Pos;
using ErpApp.Domain.Tenancy;
using FluentValidation;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.UnitTests.Pos;

/// <summary>
/// Phase 60 -- the POS foundation: a location's mode and its entitlement, per-location settings,
/// payment modes offered at a till, the walk-in customer and the three tenant-default accounts.
/// </summary>
public class PosConfigurationTests
{
    private static readonly AccountingFeatureSelections NoPos = new(
        TrackInventory: true, MultipleLocations: true, MultipleWarehouses: false,
        MultiCurrency: false, Manufacturing: false, PosRetail: false, PosRestaurant: false);

    private static readonly AccountingFeatureSelections RestaurantOnly = NoPos with { PosRestaurant = true };

    private sealed record Tenant(Guid OrganizationId, BillingLocation Location);

    private static async Task<Tenant> SeedAsync(IAppDbContext db, AccountingFeatureSelections features)
    {
        var organizationId = Guid.NewGuid();
        await TenantFeatureSeed.SeedAsync(db, organizationId, features);
        var location = BillingLocation.CreateHeadOffice(organizationId);
        db.BillingLocations.Add(location);
        await db.SaveChangesAsync();
        return new Tenant(organizationId, location);
    }

    private static async Task<Account> AddAccountAsync(
        IAppDbContext db, Guid organizationId, string code, AccountKind kind = AccountKind.Other)
    {
        var account = Account.Create(organizationId, code, $"Account {code}", AccountRootType.Asset, Guid.NewGuid(), kind);
        db.Accounts.Add(account);
        await db.SaveChangesAsync();
        return account;
    }

    private static UpdatePosLocationSettingsCommand Settings(Tenant tenant) => new(
        tenant.OrganizationId,
        tenant.Location.Id,
        ServiceChargeEnabled: true,
        ServiceChargeRate: 10m,
        ServiceChargeAccountId: null,
        ServiceChargeOnTakeAway: true,
        RoundOffEnabled: true,
        RoundOffAccountId: null,
        CashVerificationRequired: true,
        Denominations: [5, 1000, 100],
        DefaultTab: null,
        PrintEstimateBill: true,
        PrintInvoice: true,
        PrintCreditNote: false,
        PrintKot: true,
        AbbreviatedTaxInvoiceEnabled: false);

    // ---- The walk-in customer ----

    [Fact]
    public async Task Creating_an_organization_seeds_exactly_one_walk_in_customer_and_a_HeadOffice_with_no_till()
    {
        var db = TestAppDbContext.Create();
        var handler = new CreateOrganizationCommandHandler(db, new FakeCurrentUserService(Guid.NewGuid()), new FakeTurnstileVerifier());

        var result = await handler.Handle(
            new CreateOrganizationCommand(
                "Momo House", "Restaurant", "Kathmandu", new DateOnly(2026, 1, 1), true, "momo-house",
                null, null, null, null, true, false, false, false, false, false, true, "turnstile-token"),
            CancellationToken.None);

        var walkIn = Assert.Single(await db.Contacts.Where(x => x.OrganizationId == result.OrganizationId).ToListAsync());
        Assert.True(walkIn.IsWalkInCustomer);
        Assert.Equal(ContactType.Customer, walkIn.Type);
        Assert.Equal(Contact.WalkInCustomerCode, walkIn.Code);
        Assert.Equal(0m, walkIn.CreditLimit);

        var headOffice = await db.BillingLocations.SingleAsync(x => x.OrganizationId == result.OrganizationId);
        Assert.Equal(PosMode.None, headOffice.PosMode);
    }

    [Fact]
    public async Task The_walk_in_customer_cannot_be_deactivated()
    {
        var db = TestAppDbContext.Create();
        var walkIn = Contact.CreateWalkInCustomer(Guid.NewGuid());
        db.Contacts.Add(walkIn);
        await db.SaveChangesAsync();

        var exception = await Assert.ThrowsAsync<ConflictException>(() => new DeactivateContactCommandHandler(db).Handle(
            new DeactivateContactCommand(walkIn.OrganizationId, walkIn.Id), CancellationToken.None));

        Assert.Contains("walk-in customer", exception.Message, StringComparison.Ordinal);
        Assert.True((await db.Contacts.SingleAsync()).IsActive);
    }

    // ---- The feature gate ----

    [Fact]
    public async Task The_configuration_screen_is_refused_naming_both_POS_features_on_a_tenant_with_neither()
    {
        var db = TestAppDbContext.Create();
        var tenant = await SeedAsync(db, NoPos);
        var behavior = new FeatureGateBehavior<GetPosConfigurationQuery, PosConfigurationDto>(db);

        var exception = await Assert.ThrowsAsync<FeatureNotEnabledException>(() => behavior.Handle(
            new GetPosConfigurationQuery(tenant.OrganizationId),
            () => throw new InvalidOperationException("next() should not have been called."),
            CancellationToken.None));

        Assert.Contains("Point of Sale (Retail) or Point of Sale (Restaurant)", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Either_POS_feature_alone_opens_the_configuration_screen()
    {
        var db = TestAppDbContext.Create();
        var tenant = await SeedAsync(db, RestaurantOnly);
        db.Contacts.Add(Contact.CreateWalkInCustomer(tenant.OrganizationId));
        await db.SaveChangesAsync();
        var behavior = new FeatureGateBehavior<GetPosConfigurationQuery, PosConfigurationDto>(db);
        var handler = new GetPosConfigurationQueryHandler(db);
        var query = new GetPosConfigurationQuery(tenant.OrganizationId);

        var result = await behavior.Handle(query, () => handler.Handle(query, CancellationToken.None), CancellationToken.None);

        Assert.False(result.PosRetailEnabled);
        Assert.True(result.PosRestaurantEnabled);
        Assert.Equal(Contact.WalkInCustomerName, result.WalkInCustomer?.Name);
        var location = Assert.Single(result.Locations);
        Assert.Equal(PosMode.None, location.PosMode);
        Assert.False(location.HasSettings);
    }

    // ---- The mode ----

    [Fact]
    public async Task A_mode_needs_its_own_entitlement_and_None_needs_none()
    {
        var db = TestAppDbContext.Create();
        var tenant = await SeedAsync(db, RestaurantOnly);
        var handler = new SetLocationPosModeCommandHandler(db);

        var refused = await Assert.ThrowsAsync<FeatureNotEnabledException>(() => handler.Handle(
            new SetLocationPosModeCommand(tenant.OrganizationId, tenant.Location.Id, PosMode.Retail), CancellationToken.None));
        Assert.Contains("Point of Sale (Retail)", refused.Message, StringComparison.Ordinal);

        var restaurant = await handler.Handle(
            new SetLocationPosModeCommand(tenant.OrganizationId, tenant.Location.Id, PosMode.Restaurant), CancellationToken.None);
        Assert.Equal(PosMode.Restaurant, restaurant.PosMode);
        Assert.Equal([PosTab.DineIn, PosTab.TakeAway, PosTab.Delivery], restaurant.AvailableTabs);
        Assert.Equal(PosTab.DineIn, restaurant.EffectiveDefaultTab);

        // A tenant with no POS entitlement at all can still switch a till off (phase-20f).
        var noPos = TestAppDbContext.Create();
        var bare = await SeedAsync(noPos, NoPos);
        var off = await new SetLocationPosModeCommandHandler(noPos).Handle(
            new SetLocationPosModeCommand(bare.OrganizationId, bare.Location.Id, PosMode.None), CancellationToken.None);
        Assert.Equal(PosMode.None, off.PosMode);
    }

    [Fact]
    public async Task An_inactive_location_cannot_be_given_a_till()
    {
        var db = TestAppDbContext.Create();
        var tenant = await SeedAsync(db, RestaurantOnly);
        var branch = BillingLocation.Create(tenant.OrganizationId, "1002", "Branch", "Pokhara", null);
        branch.Update("1002", "Branch", "Pokhara", null, isActive: false);
        db.BillingLocations.Add(branch);
        await db.SaveChangesAsync();

        await Assert.ThrowsAsync<ConflictException>(() => new SetLocationPosModeCommandHandler(db).Handle(
            new SetLocationPosModeCommand(tenant.OrganizationId, branch.Id, PosMode.Restaurant), CancellationToken.None));
    }

    // ---- Settings ----

    [Fact]
    public async Task Settings_read_as_the_defaults_until_saved_and_round_trip_once_saved()
    {
        var db = TestAppDbContext.Create();
        var tenant = await SeedAsync(db, RestaurantOnly);

        var before = await new GetPosLocationSettingsQueryHandler(db).Handle(
            new GetPosLocationSettingsQuery(tenant.OrganizationId, tenant.Location.Id), CancellationToken.None);
        Assert.False(before.IsSaved);
        Assert.Equal(PosLocationSettings.DefaultDenominations, before.Denominations);
        Assert.False(before.ServiceChargeEnabled);
        Assert.Empty(await db.PosLocationSettings.ToListAsync());

        var serviceAccount = await AddAccountAsync(db, tenant.OrganizationId, "4100");
        var saved = await new UpdatePosLocationSettingsCommandHandler(db).Handle(
            Settings(tenant) with { ServiceChargeAccountId = serviceAccount.Id }, CancellationToken.None);

        Assert.True(saved.IsSaved);
        Assert.Equal(10m, saved.ServiceChargeRate);
        Assert.Equal(serviceAccount.Id, saved.ServiceChargeAccountId);
        Assert.Equal([1000, 100, 5], saved.Denominations);
        Assert.False(saved.PrintCreditNote);

        // A second save updates the one row rather than adding another; switching service charge
        // off clears its rate and account together.
        var again = await new UpdatePosLocationSettingsCommandHandler(db).Handle(
            Settings(tenant) with { ServiceChargeEnabled = false, ServiceChargeAccountId = serviceAccount.Id },
            CancellationToken.None);
        Assert.Equal(0m, again.ServiceChargeRate);
        Assert.Null(again.ServiceChargeAccountId);
        Assert.Single(await db.PosLocationSettings.ToListAsync());

        var reread = await new GetPosLocationSettingsQueryHandler(db).Handle(
            new GetPosLocationSettingsQuery(tenant.OrganizationId, tenant.Location.Id), CancellationToken.None);
        IReadOnlyList<int> none = [];
        Assert.Equal(again with { Denominations = none },
            reread with { Denominations = none, AvailableTabs = again.AvailableTabs, PaymentModeIds = again.PaymentModeIds });
        Assert.Equal(again.Denominations, reread.Denominations);
    }

    [Fact]
    public async Task A_default_tab_must_belong_to_the_locations_mode()
    {
        var db = TestAppDbContext.Create();
        var tenant = await SeedAsync(db, RestaurantOnly);
        var handler = new UpdatePosLocationSettingsCommandHandler(db);

        // No till yet: no tab is valid.
        var noTill = await Assert.ThrowsAsync<ValidationException>(() => handler.Handle(
            Settings(tenant) with { DefaultTab = PosTab.DineIn }, CancellationToken.None));
        Assert.Contains("Choose a POS mode first", noTill.Message, StringComparison.Ordinal);

        await new SetLocationPosModeCommandHandler(db).Handle(
            new SetLocationPosModeCommand(tenant.OrganizationId, tenant.Location.Id, PosMode.Restaurant), CancellationToken.None);

        await Assert.ThrowsAsync<ValidationException>(() => handler.Handle(
            Settings(tenant) with { DefaultTab = PosTab.Retail }, CancellationToken.None));

        var takeAway = await handler.Handle(Settings(tenant) with { DefaultTab = PosTab.TakeAway }, CancellationToken.None);
        Assert.Equal(PosTab.TakeAway, takeAway.EffectiveDefaultTab);
    }

    [Fact]
    public async Task A_settings_account_must_belong_to_the_tenant()
    {
        var db = TestAppDbContext.Create();
        var tenant = await SeedAsync(db, RestaurantOnly);
        var foreign = await AddAccountAsync(db, Guid.NewGuid(), "4100");

        await Assert.ThrowsAsync<NotFoundException>(() => new UpdatePosLocationSettingsCommandHandler(db).Handle(
            Settings(tenant) with { RoundOffAccountId = foreign.Id }, CancellationToken.None));
    }

    [Fact]
    public void The_settings_validator_names_each_field_it_refuses()
    {
        var validator = new UpdatePosLocationSettingsCommandValidator();
        var tenant = new Tenant(Guid.NewGuid(), BillingLocation.CreateHeadOffice(Guid.NewGuid()));

        Assert.True(validator.Validate(Settings(tenant)).IsValid);

        var rate = validator.Validate(Settings(tenant) with { ServiceChargeRate = 0m });
        Assert.Contains(rate.Errors, x => x.PropertyName == nameof(UpdatePosLocationSettingsCommand.ServiceChargeRate));

        // A zero rate is fine while service charge is off.
        Assert.True(validator.Validate(Settings(tenant) with { ServiceChargeEnabled = false, ServiceChargeRate = 0m }).IsValid);

        foreach (var bad in new IReadOnlyList<int>[] { [], [100, 100], [100, -5] })
        {
            var result = validator.Validate(Settings(tenant) with { Denominations = bad });
            Assert.Contains(result.Errors, x => x.PropertyName == nameof(UpdatePosLocationSettingsCommand.Denominations));
        }
    }

    // ---- Payment modes at the till ----

    [Fact]
    public async Task A_payment_mode_account_must_be_cash_or_bank()
    {
        var db = TestAppDbContext.Create();
        var organizationId = Guid.NewGuid();
        var income = await AddAccountAsync(db, organizationId, "4000");
        var cash = await AddAccountAsync(db, organizationId, "1000", AccountKind.Cash);
        var handler = new CreatePaymentModeCommandHandler(db);

        await Assert.ThrowsAsync<ValidationException>(() => handler.Handle(
            new CreatePaymentModeCommand(organizationId, "Cash", Kind: PaymentModeKind.Cash, AccountId: income.Id),
            CancellationToken.None));

        var created = await handler.Handle(
            new CreatePaymentModeCommand(organizationId, "Cash", Kind: PaymentModeKind.Cash, AccountId: cash.Id),
            CancellationToken.None);
        Assert.Equal(PaymentModeKind.Cash, created.Kind);
        Assert.Equal(cash.Id, created.AccountId);
    }

    [Fact]
    public async Task Only_an_active_mode_with_an_account_can_be_offered_and_the_set_is_diffed_not_replaced()
    {
        var db = TestAppDbContext.Create();
        var tenant = await SeedAsync(db, RestaurantOnly);
        var cashAccount = await AddAccountAsync(db, tenant.OrganizationId, "1000", AccountKind.Cash);
        var cash = PaymentMode.Create(tenant.OrganizationId, "Cash", kind: PaymentModeKind.Cash, accountId: cashAccount.Id);
        var card = PaymentMode.Create(tenant.OrganizationId, "Card", kind: PaymentModeKind.Card, accountId: cashAccount.Id);
        var unposted = PaymentMode.Create(tenant.OrganizationId, "Voucher", kind: PaymentModeKind.Other);
        db.PaymentModes.AddRange(cash, card, unposted);
        await db.SaveChangesAsync();
        var handler = new SetPosLocationPaymentModesCommandHandler(db);

        var refused = await Assert.ThrowsAsync<ValidationException>(() => handler.Handle(
            new SetPosLocationPaymentModesCommand(tenant.OrganizationId, tenant.Location.Id, [cash.Id, unposted.Id]),
            CancellationToken.None));
        Assert.Contains("'Voucher'", refused.Message, StringComparison.Ordinal);
        Assert.Empty(await db.PosLocationPaymentModes.ToListAsync());

        await handler.Handle(
            new SetPosLocationPaymentModesCommand(tenant.OrganizationId, tenant.Location.Id, [cash.Id, card.Id]),
            CancellationToken.None);
        var cashLink = await db.PosLocationPaymentModes.SingleAsync(x => x.PaymentModeId == cash.Id);

        var result = await handler.Handle(
            new SetPosLocationPaymentModesCommand(tenant.OrganizationId, tenant.Location.Id, [cash.Id]),
            CancellationToken.None);

        Assert.Equal([cash.Id], result.PaymentModeIds);
        Assert.Equal(cashLink.Id, (await db.PosLocationPaymentModes.SingleAsync()).Id);
    }

    [Fact]
    public async Task A_linked_mode_cannot_be_edited_into_one_the_till_could_not_post()
    {
        var db = TestAppDbContext.Create();
        var tenant = await SeedAsync(db, RestaurantOnly);
        var cashAccount = await AddAccountAsync(db, tenant.OrganizationId, "1000", AccountKind.Cash);
        var cash = PaymentMode.Create(tenant.OrganizationId, "Cash", kind: PaymentModeKind.Cash, accountId: cashAccount.Id);
        db.PaymentModes.Add(cash);
        db.PosLocationPaymentModes.Add(PosLocationPaymentMode.Create(tenant.OrganizationId, tenant.Location.Id, cash.Id));
        await db.SaveChangesAsync();
        var handler = new UpdatePaymentModeCommandHandler(db);

        await Assert.ThrowsAsync<ConflictException>(() => handler.Handle(
            new UpdatePaymentModeCommand(tenant.OrganizationId, cash.Id, "Cash", true, false, PaymentModeKind.Cash, AccountId: null),
            CancellationToken.None));
        await Assert.ThrowsAsync<ConflictException>(() => handler.Handle(
            new UpdatePaymentModeCommand(tenant.OrganizationId, cash.Id, "Cash", false, false, PaymentModeKind.Cash, cashAccount.Id),
            CancellationToken.None));

        var renamed = await handler.Handle(
            new UpdatePaymentModeCommand(tenant.OrganizationId, cash.Id, "Cash Drawer", true, false, PaymentModeKind.Cash, cashAccount.Id),
            CancellationToken.None);
        Assert.Equal("Cash Drawer", renamed.Name);
    }

    // ---- Tenant-default accounts ----

    [Fact]
    public async Task The_three_POS_default_accounts_round_trip_through_accounting_defaults()
    {
        var db = TestAppDbContext.Create();
        var organizationId = Guid.NewGuid();
        db.TenantSettings.Add(TenantSettings.CreateDefault(organizationId));
        var serviceCharge = await AddAccountAsync(db, organizationId, "4100");
        var rounding = await AddAccountAsync(db, organizationId, "4900");
        var overShort = await AddAccountAsync(db, organizationId, "6900");

        await new UpdateAccountingDefaultsCommandHandler(db).Handle(
            new UpdateAccountingDefaultsCommand(
                organizationId, null, null, null, null, null, null, null, null, null, null, null,
                DefaultServiceChargeAccountId: serviceCharge.Id,
                DefaultRoundingAccountId: rounding.Id,
                DefaultCashOverShortAccountId: overShort.Id),
            CancellationToken.None);

        var read = await new GetAccountingDefaultsQueryHandler(db).Handle(
            new GetAccountingDefaultsQuery(organizationId), CancellationToken.None);
        Assert.Equal(serviceCharge.Id, read.DefaultServiceChargeAccountId);
        Assert.Equal(rounding.Id, read.DefaultRoundingAccountId);
        Assert.Equal(overShort.Id, read.DefaultCashOverShortAccountId);
    }
}
