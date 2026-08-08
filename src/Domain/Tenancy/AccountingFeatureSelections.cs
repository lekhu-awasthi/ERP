namespace ErpApp.Domain.Tenancy;

/// <summary>
/// The Step 2 "Accounting Features" checkbox selections from the New Organization wizard
/// (erp-module-scan.md's Signup & Onboarding section) -- bundled as one parameter object rather
/// than seven positional bools, since it travels together from command through to
/// <see cref="TenantSubscription.CreateTrial"/>.
/// </summary>
public readonly record struct AccountingFeatureSelections(
    bool TrackInventory,
    bool MultipleLocations,
    bool MultipleWarehouses,
    bool MultiCurrency,
    bool Manufacturing,
    bool PosRetail,
    bool PosRestaurant);
