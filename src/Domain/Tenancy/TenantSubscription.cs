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

    /// <summary>
    /// Phase 31 -- the mutator phase 20f deliberately left out. Renews or extends the subscription:
    /// the plan's name and the date past which
    /// <c>SubscriptionExpiryBehavior</c> makes this organization read-only for business documents.
    ///
    /// <para><b>It moves the end date and the plan name, and nothing else.</b> The entitlement flags
    /// stay immutable after creation, exactly as 20f established and as the reference product's own
    /// read-only Features screen shows -- a renewal is a billing event, not a re-negotiation of what
    /// the tenant may model, and letting it flip <c>TrackInventoryEnabled</c> would let a tenant
    /// with stock already in a FIFO ledger turn inventory off underneath it.</para>
    ///
    /// <para><c>TrialStartsAt</c> is likewise untouched: it records when this tenant began, which a
    /// renewal does not change.</para>
    /// </summary>
    public void Renew(string planName, DateTimeOffset endsAt)
    {
        if (string.IsNullOrWhiteSpace(planName))
        {
            throw new InvalidOperationException("A subscription needs a plan name.");
        }

        if (endsAt <= TrialStartsAt)
        {
            throw new InvalidOperationException("A subscription cannot end before it started.");
        }

        PlanName = planName.Trim();
        TrialEndsAt = endsAt;
    }

    /// <summary>
    /// Whether this tenant opted into <paramref name="feature"/> at Organization creation
    /// (FR-2.6, enforced since Phase 20f). The single place the <see cref="TenantFeature"/> enum
    /// maps back onto the flag columns, so FeatureGateBehavior and the read-only subscription
    /// query can't disagree about which column a feature means.
    ///
    /// These flags are immutable after creation, matching the reference product: its own
    /// Configurations &gt; Tigg Subscriptions screen renders them as read-only rows, and a
    /// disabled feature's panel says to contact vendor support to activate it -- there is no
    /// self-service toggle. See phase-20f-status.md's confirm-live findings.
    /// </summary>
    public bool IsEnabled(TenantFeature feature)
    {
        return feature switch
        {
            TenantFeature.TrackInventory => TrackInventoryEnabled,
            TenantFeature.MultipleLocations => MultipleLocationsEnabled,
            TenantFeature.MultipleWarehouses => MultipleWarehousesEnabled,
            TenantFeature.MultiCurrency => MultiCurrencyEnabled,
            TenantFeature.Manufacturing => ManufacturingEnabled,
            TenantFeature.PosRetail => PosRetailEnabled,
            TenantFeature.PosRestaurant => PosRestaurantEnabled,
            _ => throw new ArgumentOutOfRangeException(nameof(feature), feature, "Unknown tenant feature."),
        };
    }
}
