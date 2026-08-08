namespace ErpApp.Domain.Tenancy;

/// <summary>
/// Read-mostly per-tenant subscription (architecture-spec.md §4.1): a 15-day trial seeded at
/// Organization creation (confirmed live in erp-module-scan.md's Signup & Onboarding section),
/// plus the entitlement flags gating command availability elsewhere -- the Step 2 "Accounting
/// Features" wizard checkboxes become these flags, one-time opt-ins made at creation.
/// </summary>
public sealed class TenantSubscription
{
    public Guid Id { get; private set; }
    public Guid OrganizationId { get; private set; }
    public string PlanName { get; private set; } = null!;
    public DateTimeOffset TrialStartsAt { get; private set; }
    public DateTimeOffset TrialEndsAt { get; private set; }
    public bool TrackInventoryEnabled { get; private set; }
    public bool MultipleLocationsEnabled { get; private set; }
    public bool MultipleWarehousesEnabled { get; private set; }
    public bool MultiCurrencyEnabled { get; private set; }
    public bool ManufacturingEnabled { get; private set; }
    public bool PosRetailEnabled { get; private set; }
    public bool PosRestaurantEnabled { get; private set; }
    // Reserved per architecture-spec.md §4.1/§6 -- no IRD e-filing integration designed yet,
    // so this can never be enabled at creation; a later phase flips it on once that's built.
    public bool IrdSyncEnabled { get; private set; }

    private TenantSubscription()
    {
    }

    public static TenantSubscription CreateTrial(Guid organizationId, AccountingFeatureSelections features)
    {
        var now = DateTimeOffset.UtcNow;

        return new TenantSubscription
        {
            Id = Guid.NewGuid(),
            OrganizationId = organizationId,
            PlanName = "Trial",
            TrialStartsAt = now,
            TrialEndsAt = now.AddDays(15),
            TrackInventoryEnabled = features.TrackInventory,
            MultipleLocationsEnabled = features.MultipleLocations,
            MultipleWarehousesEnabled = features.MultipleWarehouses,
            MultiCurrencyEnabled = features.MultiCurrency,
            ManufacturingEnabled = features.Manufacturing,
            PosRetailEnabled = features.PosRetail,
            PosRestaurantEnabled = features.PosRestaurant,
            IrdSyncEnabled = false,
        };
    }
}
