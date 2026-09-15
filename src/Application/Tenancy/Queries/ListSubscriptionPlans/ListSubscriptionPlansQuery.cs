using ErpApp.Application.Common.Security;
using MediatR;

namespace ErpApp.Application.Tenancy.Queries.ListSubscriptionPlans;

/// <summary>
/// Phase 41 -- the vendor's plan catalogue, which the Subscription screen's picker renders instead of
/// phase 31's free-text plan name.
///
/// <para><b>Organization-scoped even though the data is not.</b> Every plan row is identical for every
/// tenant. The query still takes an <c>OrganizationId</c> and a permission key, because in this
/// codebase <c>AuthorizationBehavior</c> is the only thing that verifies org membership at all, and a
/// query that skipped <see cref="IOrganizationScoped"/> would be reachable by any authenticated user
/// of any tenant. Price lists are public -- they are on the seller's website -- so nothing is
/// protected here; the scoping exists so this query cannot become the exception that teaches the next
/// one to skip it (phase 12's lesson).</para>
///
/// <para>Reuses <c>SubscriptionView</c> rather than adding a key. That key is already seeded to Admin
/// <i>and</i> Member because the Angular shell reads the subscription to decide which feature-gated
/// nav entries to render; seeing what plans exist is strictly less than that. Choosing one is gated
/// separately, by <c>SubscriptionManage</c> on the command.</para>
/// </summary>
public sealed record ListSubscriptionPlansQuery(Guid OrganizationId)
    : IRequest<IReadOnlyList<SubscriptionPlanDto>>, IRequirePermission, IOrganizationScoped
{
    public string PermissionKey => PermissionKeys.SubscriptionView;
}

/// <param name="AnnualAmount">The published list price. What a tenant actually pays is recorded on
/// its own subscription -- see <c>TenantSubscription.SubscriptionAmount</c>.</param>
/// <param name="IncludedFeatures">What the tier publishes, for the picker's tick/cross list. These do
/// not flip the tenant's entitlement flags; see <c>TenantSubscription.SetPlan</c>.</param>
public sealed record SubscriptionPlanDto(
    Guid Id,
    string Code,
    string Name,
    string Description,
    decimal AnnualAmount,
    int ProductQuota,
    int TransactionQuota,
    int DailyAiScanQuota,
    IReadOnlyList<SubscriptionPlanFeatureDto> IncludedFeatures);

public sealed record SubscriptionPlanFeatureDto(string Name, bool IsIncluded);
