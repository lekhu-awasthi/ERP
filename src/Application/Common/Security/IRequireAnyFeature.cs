using ErpApp.Domain.Tenancy;

namespace ErpApp.Application.Common.Security;

/// <summary>
/// Phase 60 -- the any-of counterpart of <see cref="IRequireFeature"/>, checked by the same
/// FeatureGateBehavior: the request proceeds when the tenant has <b>at least one</b> of
/// <see cref="AnyOfFeatures"/>. Point-of-sale configuration is the case: a tenant that bought POS
/// Retail and one that bought POS Restaurant both configure locations, payment modes and service
/// charge, and a tenant with neither has no till to configure.
///
/// <para>A second marker rather than a mode on the first, because <see cref="IRequireFeature"/>'s
/// all-of reading is what WarehouseTransfer depends on, and a request can declare both. Kept in the
/// pipeline rather than written into each handler: the gate is unconditional on the request's
/// values, which is the test phase-20f Decision #4 uses for "behavior, not handler". Which POS mode a
/// location may be <i>given</i> depends on the value asked for, so that one check stays in its
/// handler.</para>
///
/// <para>The same rule as the first marker: every implementer is also
/// <see cref="IOrganizationScoped"/>, or the behavior throws.</para>
/// </summary>
public interface IRequireAnyFeature
{
    IReadOnlyCollection<TenantFeature> AnyOfFeatures { get; }
}

/// <summary>Phase 60 -- the one any-of set in use: either POS entitlement.</summary>
public static class PosFeatures
{
    public static readonly IReadOnlyCollection<TenantFeature> Any = [TenantFeature.PosRetail, TenantFeature.PosRestaurant];
}
