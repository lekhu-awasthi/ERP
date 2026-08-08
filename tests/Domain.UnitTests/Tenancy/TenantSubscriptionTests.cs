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
}
