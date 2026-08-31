namespace ErpApp.Domain.Tenancy;

/// <summary>
/// The tenant entitlements captured once at Organization creation from the New Organization
/// wizard's Step 2 "Accounting Features" checkboxes (erp-module-scan.md's Signup &amp; Onboarding
/// section) and enforced at point of use since Phase 20f (FR-2.6). One member per
/// <see cref="AccountingFeatureSelections"/> field, in the same order.
///
/// An enum rather than the string constants <see cref="Application"/>-layer PermissionKeys uses:
/// permission keys are strings because each one is *persisted* as a RolePermission row, so the
/// key itself is data. A feature flag is a fixed column on <see cref="TenantSubscription"/> --
/// nothing persists the flag's name -- so there's no reason to give up compile-time checking.
///
/// <see cref="TenantSubscription.IrdSyncEnabled"/> deliberately has no member here: it can never
/// be enabled at creation (no IRD e-filing integration is designed yet), so nothing could gate
/// on it. Add it when that phase lands.
/// </summary>
public enum TenantFeature
{
    TrackInventory = 1,
    MultipleLocations = 2,
    MultipleWarehouses = 3,
    MultiCurrency = 4,
    Manufacturing = 5,
    PosRetail = 6,
    PosRestaurant = 7,
}
