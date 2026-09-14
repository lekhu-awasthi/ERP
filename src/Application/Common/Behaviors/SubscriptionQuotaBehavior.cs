using ErpApp.Application.Common.Exceptions;
using ErpApp.Application.Common.Persistence;
using ErpApp.Application.Common.Security;
using ErpApp.Application.Tenancy;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.Common.Behaviors;

/// <summary>
/// Phase 41 -- the sixth pipeline behavior, and the one that makes the plan catalogue mean something:
/// it refuses a command once the tenant has spent the allowance its plan sold it.
///
/// <para><b>Two ceilings, from the vendor's own price list</b> (tiggapp.com/pricing, read
/// 2026-09-14): transactions per subscription term (Basic 30,000, Standard 50,000, Professional
/// 200,000) and products (1,000 / 5,000 / 10,000), each extensible by an add-on. A stored ceiling of
/// <c>0</c> means not metered, which is every trial and every tenant created before this phase, so
/// the overwhelmingly common path through this behavior is two field reads and a delegate call.</para>
///
/// <para><b>Why a behavior rather than a check in eleven handlers.</b> The same reason phase 31 gave
/// for expiry and phase 20f for entitlements: the question is "may this organization do this at all",
/// which is not any one handler's business, and eleven copies is eleven chances for the twelfth
/// document type to be added without one. The set is a marker interface, swept in both directions by
/// <c>MeteredTransactionSweepGuardTests</c>.</para>
///
/// <para><b>Registration order.</b> After <c>AuthorizationBehavior</c> so a non-member learns nothing
/// about the tenant's commercial state, and immediately after <c>SubscriptionExpiryBehavior</c>: an
/// expired subscription is the more fundamental refusal and must win, or a tenant whose term has
/// ended would be told it is out of transactions -- true but beside the point, and it would send them
/// to buy the wrong thing.</para>
///
/// <para><b>What this behavior cannot do, stated plainly.</b> <c>Tenancy.Subscription.Manage</c> is
/// seeded to the tenant's own Admin role, so the party this ceiling constrains can raise it: one
/// <c>PUT /subscription</c> sets any quota and any end date. The ceiling is therefore an accurate
/// record of what was sold and an honest guard against drifting past it unnoticed -- not a control
/// that survives an adversary. Fixing that needs a vendor actor, which this codebase does not model;
/// it is phase 41's headline carried item and the re-entry condition is written up in
/// docs/phase-41-status.md.</para>
/// </summary>
public sealed class SubscriptionQuotaBehavior<TRequest, TResponse>(IAppDbContext db)
    : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
{
    public async Task<TResponse> Handle(
        TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
    {
        if (request is not (IMeteredTransaction or IMeteredProduct))
        {
            return await next();
        }

        // Same shape as FeatureGateBehavior's: a metered request with no organization has no tenant
        // whose ceiling could be read, so the gate would no-op forever without anything failing.
        if (request is not IOrganizationScoped scoped)
        {
            throw new InvalidOperationException(
                $"{typeof(TRequest).Name} is metered but not IOrganizationScoped, so its tenant's "
                + "subscription quotas cannot be resolved. Every metered request must be organization-scoped.");
        }

        var subscription = await db.TenantSubscriptions
            .AsNoTracking()
            .SingleOrDefaultAsync(x => x.OrganizationId == scoped.OrganizationId, cancellationToken);

        // A tenant with no subscription row is not metered, matching SubscriptionExpiryBehavior's
        // reading of the same absence: the row is seeded at Organization creation, so a missing one
        // means a fixture or a partially migrated tenant, and failing those closed would break far
        // more than it protects. FeatureGateBehavior fails the other way on the same absence
        // deliberately -- "no entitlements recorded" must mean none, while "no ceiling recorded"
        // genuinely is no ceiling.
        if (subscription is null)
        {
            return await next();
        }

        if (request is IMeteredTransaction && subscription.TransactionQuota > 0)
        {
            var used = await SubscriptionUsageReader.CountTransactionsAsync(db, subscription, cancellationToken);

            // >= , not > : the ceiling is how many the tenant may have, so the request that would
            // make it one more than that is the one to refuse.
            if (used >= subscription.TransactionQuota)
            {
                throw new SubscriptionQuotaExceededException(
                    SubscriptionQuotaKind.Transactions, subscription.PlanName, used, subscription.TransactionQuota);
            }
        }

        if (request is IMeteredProduct && subscription.ProductQuota > 0)
        {
            var used = await SubscriptionUsageReader.CountProductsAsync(db, subscription, cancellationToken);

            if (used >= subscription.ProductQuota)
            {
                throw new SubscriptionQuotaExceededException(
                    SubscriptionQuotaKind.Products, subscription.PlanName, used, subscription.ProductQuota);
            }
        }

        return await next();
    }
}
