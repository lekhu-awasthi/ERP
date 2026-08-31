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

public sealed record TenantSubscriptionDto(
    Guid OrganizationId,
    string PlanName,
    DateTimeOffset TrialStartsAt,
    DateTimeOffset TrialEndsAt,
    bool IsTrialActive,
    int DaysRemaining,
    bool IrdSyncEnabled,
    IReadOnlyList<TenantFeatureStateDto> Features);
