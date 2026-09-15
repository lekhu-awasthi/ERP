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
/// Phase 46 (phase 41 carried item #5) -- one entitlement on which the tenant's recorded tier and
/// the tenant's actual flags disagree.
///
/// <para><b>Surfaced, never reconciled.</b> Phase 31's rule stands: a plan change does not touch the
/// entitlement flags, because a billing event is not a re-negotiation of what the tenant may model
/// and flipping <c>TrackInventoryEnabled</c> off under a tenant with stock already in a FIFO ledger
/// is the failure that prevents. So a mismatch is a real, legitimate state -- and until this phase
/// nothing anywhere said it existed.</para>
///
/// <para><b>Computed here rather than in the browser</b>, and that is not a preference. The plan's
/// tick-list is the price list's wording ("Multiple currency", "POS (Retail/Restro)") while a
/// tenant's features carry the signup wizard's ("Multi-Currency Support", "Point of Sale (Retail)"),
/// and the two lists are not even the same length -- one POS row covers two tenant flags, Landed
/// Cost and Developer API have no tenant flag at all, and Multiple Locations is an add-on no tier
/// includes. Joining those two vocabularies by display name silently matches nothing, which is the
/// same class of mistake as phase 27a's ordinal enum bridge and fails the same way: quietly, and
/// looking like "no mismatches".</para>
/// </summary>
/// <param name="HeldButNotIncluded">The tenant has it; the recorded tier does not sell it.</param>
public sealed record EntitlementMismatchDto(string Feature, string DisplayName, bool HeldButNotIncluded);

/// <summary>
/// Phase 41 -- what the tenant has consumed of each metered allowance, alongside the ceiling. Both
/// halves come from <c>SubscriptionUsageReader</c>, the same code <c>SubscriptionQuotaBehavior</c>
/// blocks on, so the bar this screen draws and the refusal an Approve gets can never disagree.
/// A quota of <c>0</c> means not metered and the screen says so rather than drawing a full bar.
/// </summary>
/// <param name="AiScansUsed">Phase 46 -- extractions already run today (Nepal local). Its ceiling is
/// per day and is not purchasable, so a screen showing it must not offer an upgrade beside it.</param>
/// <param name="LocationsUsed">Phase 46 -- billing locations in use. Reported beside
/// <paramref name="LocationQuota"/>, the purchased count, purely so the two can be compared by a
/// reader; nothing in the product refuses on it.</param>
public sealed record SubscriptionUsageDto(
    int TransactionsUsed,
    int TransactionQuota,
    int ProductsUsed,
    int ProductQuota,
    int AiScansUsed,
    int DailyAiScanQuota,
    int LocationsUsed,
    int LocationQuota);

/// <param name="PlanId">The catalogue row this tenant is on, or null while on the seeded trial.</param>
/// <param name="TermEndsAt">The end of the current term, trial or paid -- see
/// <c>TenantSubscription.TermEndsAt</c> on why the name still says trial.</param>
/// <param name="SubscriptionAmount">Phase 41 -- what this tenant is charged, which phase 33 read as
/// a dead column on two tenants that were both free trials.</param>
/// <param name="IrdVerified">Phase 41 -- the IRD Billing add-on, shown as the reference product's own
/// IRD Verified row. Not the same thing as <paramref name="IrdSyncEnabled"/>.</param>
public sealed record TenantSubscriptionDto(
    Guid OrganizationId,
    Guid? PlanId,
    string PlanName,
    DateTimeOffset OriginatedAt,
    DateTimeOffset TermStartsAt,
    DateTimeOffset TermEndsAt,
    bool IsTrialActive,
    int DaysRemaining,
    decimal SubscriptionAmount,
    bool IrdVerified,
    bool IrdSyncEnabled,
    DateTimeOffset AllowanceYearStartsAt,
    DateTimeOffset AllowanceYearEndsAt,
    SubscriptionUsageDto Usage,
    IReadOnlyList<TenantFeatureStateDto> Features,
    IReadOnlyList<EntitlementMismatchDto> EntitlementMismatches);
