using ErpApp.Domain.Tenancy;

namespace ErpApp.Domain.UnitTests.Tenancy;

public class TenantSubscriptionTests
{
    [Fact]
    public void CreateTrial_sets_a_fifteen_day_trial_window()
    {
        var subscription = TenantSubscription.CreateTrial(Guid.NewGuid(), default);

        Assert.Equal("Trial", subscription.PlanName);
        Assert.Equal(15, (subscription.TrialEndsAt - subscription.TrialStartsAt).TotalDays, precision: 5);
    }

    [Fact]
    public void CreateTrial_carries_over_the_selected_accounting_features()
    {
        var features = new AccountingFeatureSelections(
            TrackInventory: true,
            MultipleLocations: false,
            MultipleWarehouses: true,
            MultiCurrency: false,
            Manufacturing: false,
            PosRetail: true,
            PosRestaurant: false);

        var subscription = TenantSubscription.CreateTrial(Guid.NewGuid(), features);

        Assert.True(subscription.TrackInventoryEnabled);
        Assert.False(subscription.MultipleLocationsEnabled);
        Assert.True(subscription.MultipleWarehousesEnabled);
        Assert.False(subscription.MultiCurrencyEnabled);
        Assert.False(subscription.ManufacturingEnabled);
        Assert.True(subscription.PosRetailEnabled);
        Assert.False(subscription.PosRestaurantEnabled);
        Assert.False(subscription.IrdSyncEnabled);
    }

    // Phase 20f (FR-2.6) -- IsEnabled is the single place TenantFeature maps back onto the flag
    // columns, so FeatureGateBehavior and the read-only subscription query can't disagree about
    // which column a feature means. A per-feature test rather than one loop, so a mis-wired
    // switch arm names the feature it got wrong.

    [Theory]
    [InlineData(TenantFeature.TrackInventory)]
    [InlineData(TenantFeature.MultipleLocations)]
    [InlineData(TenantFeature.MultipleWarehouses)]
    [InlineData(TenantFeature.MultiCurrency)]
    [InlineData(TenantFeature.Manufacturing)]
    [InlineData(TenantFeature.PosRetail)]
    [InlineData(TenantFeature.PosRestaurant)]
    public void IsEnabled_is_false_for_every_feature_when_nothing_was_opted_into(TenantFeature feature)
    {
        var subscription = TenantSubscription.CreateTrial(Guid.NewGuid(), default);

        Assert.False(subscription.IsEnabled(feature));
    }

    [Theory]
    [InlineData(TenantFeature.TrackInventory)]
    [InlineData(TenantFeature.MultipleLocations)]
    [InlineData(TenantFeature.MultipleWarehouses)]
    [InlineData(TenantFeature.MultiCurrency)]
    [InlineData(TenantFeature.Manufacturing)]
    [InlineData(TenantFeature.PosRetail)]
    [InlineData(TenantFeature.PosRestaurant)]
    public void IsEnabled_is_true_for_every_feature_when_all_were_opted_into(TenantFeature feature)
    {
        var subscription = TenantSubscription.CreateTrial(Guid.NewGuid(), new AccountingFeatureSelections(
            true, true, true, true, true, true, true));

        Assert.True(subscription.IsEnabled(feature));
    }

    [Fact]
    public void IsEnabled_reads_each_feature_from_its_own_column()
    {
        // Every flag distinct from its neighbours, so a switch arm reading the wrong column shows
        // up as a failure rather than coincidentally matching.
        var subscription = TenantSubscription.CreateTrial(Guid.NewGuid(), new AccountingFeatureSelections(
            TrackInventory: true,
            MultipleLocations: false,
            MultipleWarehouses: true,
            MultiCurrency: false,
            Manufacturing: true,
            PosRetail: false,
            PosRestaurant: true));

        Assert.True(subscription.IsEnabled(TenantFeature.TrackInventory));
        Assert.False(subscription.IsEnabled(TenantFeature.MultipleLocations));
        Assert.True(subscription.IsEnabled(TenantFeature.MultipleWarehouses));
        Assert.False(subscription.IsEnabled(TenantFeature.MultiCurrency));
        Assert.True(subscription.IsEnabled(TenantFeature.Manufacturing));
        Assert.False(subscription.IsEnabled(TenantFeature.PosRetail));
        Assert.True(subscription.IsEnabled(TenantFeature.PosRestaurant));
    }

    [Fact]
    public void IsEnabled_throws_for_a_feature_value_outside_the_enum()
    {
        var subscription = TenantSubscription.CreateTrial(Guid.NewGuid(), default);

        Assert.Throws<ArgumentOutOfRangeException>(() => subscription.IsEnabled((TenantFeature)999));
    }
}
