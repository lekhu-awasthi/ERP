using ErpApp.Application.Common.Behaviors;
using ErpApp.Application.Common.Exceptions;
using ErpApp.Application.Common.Persistence;
using ErpApp.Application.Sales;
using ErpApp.Application.Sales.Commands.CreateInvoice;
using ErpApp.Application.Tenancy.Commands.SetTenantSubscription;
using ErpApp.Application.Tenancy.Commands.UpdateGeneralSettings;
using ErpApp.Application.Tenancy.Queries.GetGeneralSettings;
using ErpApp.Application.UnitTests.TestSupport;
using ErpApp.Domain.Catalog;
using ErpApp.Domain.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.UnitTests.CreditControl;

/// <summary>
/// Phase 31 -- the Configurations &gt; General screen's command/query pair, and
/// <c>SubscriptionExpiryBehavior</c>. Four of the five settings had been schema'd since phase 2 with
/// no way to read or write them at all.
/// </summary>
public class GeneralSettingsAndSubscriptionTests
{
    [Fact]
    public async Task Every_general_setting_round_trips()
    {
        var db = TestAppDbContext.Create();
        var seed = await CreditLimitTestSeed.SeedAsync(db);

        await new UpdateGeneralSettingsCommandHandler(db).Handle(
            new UpdateGeneralSettingsCommand(
                seed.OrganizationId,
                SuggestSellingPriceMode.FixedSellingPrice,
                ProductPriceBasis.InclusiveOfVat,
                InventoryTrackingMode.PhysicalMovement,
                BalanceAction.DoNothing,
                BalanceAction.Reject,
                BalanceAction.DoNothing),
            CancellationToken.None);

        var read = await new GetGeneralSettingsQueryHandler(db).Handle(
            new GetGeneralSettingsQuery(seed.OrganizationId), CancellationToken.None);

        Assert.Equal(SuggestSellingPriceMode.FixedSellingPrice, read.SuggestSellingPriceMode);
        Assert.Equal(ProductPriceBasis.InclusiveOfVat, read.ProductPriceBasis);
        Assert.Equal(InventoryTrackingMode.PhysicalMovement, read.InventoryTrackingMode);
        Assert.Equal(BalanceAction.DoNothing, read.NegativeCashBalanceAction);
        Assert.Equal(BalanceAction.Reject, read.NegativeStockBalanceAction);
        Assert.Equal(BalanceAction.DoNothing, read.CreditLimitExceedsAction);
    }

    /// <summary>
    /// The whole point of the behavior: an expired organization cannot write a business document.
    /// The marker set it gates is the lock-date one, so this is the same request shape phase 16a
    /// freezes.
    /// </summary>
    [Fact]
    public async Task An_expired_subscription_blocks_a_document_command()
    {
        var db = TestAppDbContext.Create();
        var seed = await CreditLimitTestSeed.SeedAsync(db);
        await ExpireAsync(db, seed.OrganizationId);

        var behavior = new SubscriptionExpiryBehavior<CreateInvoiceCommand, CreateInvoiceResult>(db);

        await Assert.ThrowsAsync<ConflictException>(() => behavior.Handle(
            DraftCommand(seed),
            () => Task.FromResult(new CreateInvoiceResult(Guid.NewGuid(), "X", Domain.Sales.InvoiceStatus.Draft)),
            CancellationToken.None));
    }

    [Fact]
    public async Task A_live_subscription_lets_the_same_command_through()
    {
        var db = TestAppDbContext.Create();
        var seed = await CreditLimitTestSeed.SeedAsync(db);
        db.TenantSubscriptions.Add(TenantSubscription.CreateTrial(seed.OrganizationId, default(AccountingFeatureSelections)));
        await db.SaveChangesAsync(CancellationToken.None);

        var behavior = new SubscriptionExpiryBehavior<CreateInvoiceCommand, CreateInvoiceResult>(db);
        var expected = new CreateInvoiceResult(Guid.NewGuid(), "X", Domain.Sales.InvoiceStatus.Draft);

        var actual = await behavior.Handle(DraftCommand(seed), () => Task.FromResult(expected), CancellationToken.None);

        Assert.Same(expected, actual);
    }

    /// <summary>
    /// Read-only has to mean readable, and it also has to be escapable: a query and the renewal
    /// command both carry neither lock-date marker, so the behavior never sees them. This pins the
    /// renewal specifically, because a tenant that could not renew from inside the product would be
    /// bricked by its own expiry.
    /// </summary>
    [Fact]
    public async Task The_renewal_command_is_never_gated_and_lifts_the_expiry()
    {
        var db = TestAppDbContext.Create();
        var seed = await CreditLimitTestSeed.SeedAsync(db);
        await ExpireAsync(db, seed.OrganizationId);

        var renewal = new SetTenantSubscriptionCommand(
            seed.OrganizationId, "Standard", DateTimeOffset.UtcNow.AddDays(30));

        // Reflection rather than a type pattern: the compiler refuses `is ILockDateSensitive` on a
        // sealed type that cannot implement it (CS8121), which is itself the guarantee -- but the
        // assertion has to survive somebody later un-sealing the record or adding the marker.
        Assert.DoesNotContain(
            renewal.GetType().GetInterfaces(),
            x => x == typeof(ErpApp.Application.Common.Security.ILockDateSensitive)
                || x == typeof(ErpApp.Application.Common.Security.ILockDateSensitiveDocument));

        var result = await new SetTenantSubscriptionCommandHandler(db).Handle(renewal, CancellationToken.None);

        Assert.True(result.IsTrialActive);
        Assert.Equal("Standard", result.PlanName);

        var behavior = new SubscriptionExpiryBehavior<CreateInvoiceCommand, CreateInvoiceResult>(db);
        var expected = new CreateInvoiceResult(Guid.NewGuid(), "X", Domain.Sales.InvoiceStatus.Draft);
        Assert.Same(expected, await behavior.Handle(
            DraftCommand(seed), () => Task.FromResult(expected), CancellationToken.None));
    }

    /// <summary>Renewal is a billing event, not a re-negotiation of what the tenant may model --
    /// the entitlement flags 20f froze stay frozen, and there is no parameter that could move
    /// them.</summary>
    [Fact]
    public async Task Renewal_leaves_the_entitlement_flags_alone()
    {
        var db = TestAppDbContext.Create();
        var seed = await CreditLimitTestSeed.SeedAsync(db);
        db.TenantSubscriptions.Add(TenantSubscription.CreateTrial(
            seed.OrganizationId, new AccountingFeatureSelections(false, false, false, false, Manufacturing: true, false, false)));
        await db.SaveChangesAsync(CancellationToken.None);

        var result = await new SetTenantSubscriptionCommandHandler(db).Handle(
            new SetTenantSubscriptionCommand(seed.OrganizationId, "Standard", DateTimeOffset.UtcNow.AddDays(365)),
            CancellationToken.None);

        Assert.True(result.Features.Single(x => x.Feature == nameof(TenantFeature.Manufacturing)).IsEnabled);
    }

    private static CreateInvoiceCommand DraftCommand(CreditSeed seed) =>
        new(seed.OrganizationId, seed.CustomerId, seed.WarehouseId, new DateOnly(2026, 1, 10), null,
            [new InvoiceLineInput(seed.ProductId, 1m, 10m, VatRate.NoVat)]);

    /// <summary>
    /// An expired subscription cannot be built through the aggregate's own API on purpose --
    /// <see cref="TenantSubscription.Renew"/> refuses an end date before the start date, and a trial
    /// created now starts now. Only the passage of time produces this state in production, so the
    /// test reaches through EF's change tracker to construct it rather than weakening that
    /// invariant to make itself easier to write.
    /// </summary>
    private static async Task ExpireAsync(IAppDbContext db, Guid organizationId)
    {
        var subscription = TenantSubscription.CreateTrial(organizationId, default(AccountingFeatureSelections));
        db.TenantSubscriptions.Add(subscription);
        await db.SaveChangesAsync(CancellationToken.None);

        var entry = ((DbContext)db).Entry(subscription);
        entry.Property(nameof(TenantSubscription.TrialStartsAt)).CurrentValue = DateTimeOffset.UtcNow.AddDays(-30);
        entry.Property(nameof(TenantSubscription.TrialEndsAt)).CurrentValue = DateTimeOffset.UtcNow.AddDays(-15);
        await db.SaveChangesAsync(CancellationToken.None);
    }
}
