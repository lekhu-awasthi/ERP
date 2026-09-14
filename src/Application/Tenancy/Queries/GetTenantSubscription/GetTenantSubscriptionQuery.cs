using ErpApp.Application.Common.Security;
using MediatR;

namespace ErpApp.Application.Tenancy.Queries.GetTenantSubscription;

/// <summary>
/// Phase 20f (FR-2.6) -- the read-only view of the tenant's plan and its opted-in Accounting
/// Features, mirroring the reference product's own Configurations &gt; Tigg Subscriptions screen
/// (plan, amount, expiry, then one row per entitlement) plus its Organization &gt; Features tab
/// (per-feature enabled/disabled state). Read-only by design: live confirmation found neither
/// screen offers the tenant any way to change an entitlement -- a disabled feature's panel says
/// to contact vendor support -- and this codebase has no vendor-support channel, so the flags
/// stay immutable after Organization creation. See phase-20f-status.md.
///
/// Also the source of truth the Angular shell uses to decide which feature-gated nav entries to
/// render, so it must be readable by every role, not just Admin (see PermissionKeys'
/// SubscriptionView note).
/// </summary>
public sealed record GetTenantSubscriptionQuery(Guid OrganizationId)
    : IRequest<TenantSubscriptionDto>, IRequirePermission, IOrganizationScoped
{
    public string PermissionKey => PermissionKeys.SubscriptionView;
}

public sealed record TenantFeatureStateDto(string Feature, string DisplayName, string Description, bool IsEnabled);

/// <summary>
/// Phase 41 -- what the tenant has consumed of each metered allowance, alongside the ceiling. Both
/// halves come from <c>SubscriptionUsageReader</c>, the same code <c>SubscriptionQuotaBehavior</c>
/// blocks on, so the bar this screen draws and the refusal an Approve gets can never disagree.
/// A quota of <c>0</c> means not metered and the screen says so rather than drawing a full bar.
/// </summary>
public sealed record SubscriptionUsageDto(
    int TransactionsUsed,
    int TransactionQuota,
    int ProductsUsed,
    int ProductQuota);

/// <param name="PlanId">The catalogue row this tenant is on, or null while on the seeded trial.</param>
/// <param name="TrialEndsAt">The end of the current term, trial or paid -- see
/// <c>TenantSubscription.TrialEndsAt</c> on why the name still says trial.</param>
/// <param name="SubscriptionAmount">Phase 41 -- what this tenant is charged, which phase 33 read as
/// a dead column on two tenants that were both free trials.</param>
/// <param name="IrdVerified">Phase 41 -- the IRD Billing add-on, shown as the reference product's own
/// IRD Verified row. Not the same thing as <paramref name="IrdSyncEnabled"/>.</param>
public sealed record TenantSubscriptionDto(
    Guid OrganizationId,
    Guid? PlanId,
    string PlanName,
    DateTimeOffset TrialStartsAt,
    DateTimeOffset TermStartsAt,
    DateTimeOffset TrialEndsAt,
    bool IsTrialActive,
    int DaysRemaining,
    decimal SubscriptionAmount,
    bool IrdVerified,
    bool IrdSyncEnabled,
    SubscriptionUsageDto Usage,
    IReadOnlyList<TenantFeatureStateDto> Features);
