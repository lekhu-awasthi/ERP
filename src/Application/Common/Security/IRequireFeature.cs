using ErpApp.Domain.Tenancy;

namespace ErpApp.Application.Common.Security;

/// <summary>
/// Declares the tenant entitlement(s) (FR-2.6, see <see cref="TenantFeature"/>) a command/query
/// requires, checked by FeatureGateBehavior in the MediatR pipeline
/// (Common/Behaviors/FeatureGateBehavior.cs). A request that doesn't implement this interface
/// skips the feature check entirely -- the same "no marker interface, no gate" pattern
/// <see cref="IRequirePermission"/>/AuthorizationBehavior and ILockDateSensitive/LockDateBehavior
/// already use.
///
/// This is an *additional*, independent check, not a replacement for
/// <see cref="IRequirePermission"/>: a request can be permitted for the acting user's role and
/// still be unavailable because the tenant never opted into the feature. Every implementer must
/// also implement <see cref="IOrganizationScoped"/> -- there is no tenant to look the flags up
/// for otherwise, and FeatureGateBehavior throws rather than silently skipping if one doesn't
/// (the phase-12 lesson: a gate that silently no-ops is worse than no gate).
///
/// A collection rather than a single feature because WarehouseTransfer genuinely needs two
/// (moving stock between warehouses requires both inventory tracking *and* more than one
/// warehouse); every other gated request today declares exactly one.
/// </summary>
public interface IRequireFeature
{
    IReadOnlyCollection<TenantFeature> RequiredFeatures { get; }
}
