using ErpApp.Application.Common.Exceptions;
using ErpApp.Application.Common.Persistence;
using ErpApp.Domain.Tenancy;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.Tenancy.Queries.GetTenantSubscription;

public sealed class GetTenantSubscriptionQueryHandler(IAppDbContext db, TimeProvider timeProvider)
    : IRequestHandler<GetTenantSubscriptionQuery, TenantSubscriptionDto>
{
    /// <summary>
    /// Display name + description per feature, taken verbatim from the New Organization wizard's
    /// own Step 2 checkbox cards, so the read-only Features screen names each entitlement exactly
    /// the way the user saw it when they chose (or skipped) it at creation.
    /// </summary>
    internal static readonly (TenantFeature Feature, string DisplayName, string Description)[] Catalog =
    [
        (TenantFeature.TrackInventory, "Track Inventory",
            "Maintain real-time stock levels, inventory values, and purchase reorder parameters."),
        (TenantFeature.MultipleLocations, "Multiple Locations",
            "Operate from more than one billing address, retail store, or distinct office branch."),
        (TenantFeature.MultipleWarehouses, "Multiple Warehouses",
            "Track stock across separate geographical hubs, distribution centers, or stores."),
        (TenantFeature.MultiCurrency, "Multi-Currency Support",
            "Transact, issue bills, and receive customer payments in foreign exchange currencies."),
        (TenantFeature.Manufacturing, "Manufacturing",
            "Track bill of materials (BOM), create manufacturing runs, and direct production processes."),
        (TenantFeature.PosRetail, "Point of Sale (Retail)",
            "Interface for physical retail counters with support for barcode scanners and cash drawers."),
        (TenantFeature.PosRestaurant, "Point of Sale (Restaurant)",
            "Interface tailored to restaurant ordering, table layouts, and kitchen ticket prints."),
    ];

    /// <summary>
    /// Phase 46 -- the entitlements a plan row and a tenant both have an opinion about, paired by
    /// <b>column</b> rather than by name.
    ///
    /// <para>Four of the eleven are deliberately absent. Landed Cost and Developer API are published
    /// per tier but have no <c>TenantSubscription</c> flag, so there is nothing to disagree with;
    /// Multiple Locations has a tenant flag but no tier includes it (it is an add-on at Rs 5,000 per
    /// location); and POS appears once on the price list but twice here, which is why it is the one
    /// entry that maps a single plan column onto two features.</para>
    /// </summary>
    private static readonly (TenantFeature Feature, Func<SubscriptionPlan, bool> Included)[] ComparableEntitlements =
    [
        (TenantFeature.TrackInventory, p => p.TrackInventoryIncluded),
        (TenantFeature.MultipleWarehouses, p => p.MultipleWarehousesIncluded),
        (TenantFeature.MultiCurrency, p => p.MultiCurrencyIncluded),
        (TenantFeature.Manufacturing, p => p.ManufacturingIncluded),
        (TenantFeature.PosRetail, p => p.PosIncluded),
        (TenantFeature.PosRestaurant, p => p.PosIncluded),
    ];

    public async Task<TenantSubscriptionDto> Handle(
        GetTenantSubscriptionQuery request, CancellationToken cancellationToken)
    {
        var subscription = await db.TenantSubscriptions.SingleOrDefaultAsync(
            x => x.OrganizationId == request.OrganizationId, cancellationToken)
            ?? throw new NotFoundException("This organization has no subscription record.");

        var now = timeProvider.GetUtcNow();
        var usage = await SubscriptionUsageReader.ReadAsync(db, subscription, now, cancellationToken);

        // Only a tenant on a catalogue plan can disagree with one. A trial has no tier to compare
        // against, which is not a mismatch -- it is the absence of a claim.
        var plan = subscription.PlanId is { } planId
            ? await db.SubscriptionPlans.AsNoTracking().SingleOrDefaultAsync(x => x.Id == planId, cancellationToken)
            : null;

        return ToDto(subscription, usage, now, plan);
    }

    /// <summary>
    /// Both directions, because the reader's next step differs: a feature held but not sold is
    /// something to pay for, and a feature sold but not held is something to ask to have switched on
    /// (entitlements are fixed at Organization creation -- phase 20f).
    /// </summary>
    internal static IReadOnlyList<EntitlementMismatchDto> CompareEntitlements(
        TenantSubscription subscription, SubscriptionPlan? plan)
    {
        if (plan is null)
        {
            return [];
        }

        return
        [
            .. ComparableEntitlements
                .Where(x => subscription.IsEnabled(x.Feature) != x.Included(plan))
                .Select(x => new EntitlementMismatchDto(
                    x.Feature.ToString(),
                    Catalog.Single(c => c.Feature == x.Feature).DisplayName,
                    subscription.IsEnabled(x.Feature))),
        ];
    }

    /// <summary>
    /// Phase 31 extracted this so <c>SetTenantSubscriptionCommandHandler</c> returns byte-identical
    /// state to what the read query would next produce -- the same reason phase 26b insisted a pair
    /// of reports agree by construction rather than by inspection.
    /// </summary>
    internal static TenantSubscriptionDto ToDto(
        TenantSubscription subscription, SubscriptionUsage usage, DateTimeOffset now, SubscriptionPlan? plan = null)
    {
        var daysRemaining = (int)Math.Ceiling((subscription.TermEndsAt - now).TotalDays);
        var (allowanceYearStart, allowanceYearEnd) = SubscriptionUsageReader.CurrentAllowanceYear(subscription, now);

        return new TenantSubscriptionDto(
            subscription.OrganizationId,
            subscription.PlanId,
            subscription.PlanName,
            subscription.OriginatedAt,
            subscription.TermStartsAt,
            subscription.TermEndsAt,
            subscription.TermEndsAt > now,
            Math.Max(daysRemaining, 0),
            subscription.SubscriptionAmount,
            subscription.IrdVerified,
            subscription.IrdSyncEnabled,
            allowanceYearStart,
            allowanceYearEnd,
            new SubscriptionUsageDto(
                usage.TransactionsUsed,
                usage.TransactionQuota,
                usage.ProductsUsed,
                usage.ProductQuota,
                usage.AiScansUsed,
                usage.DailyAiScanQuota,
                usage.LocationsUsed,
                usage.LocationQuota),
            [.. Catalog.Select(x => new TenantFeatureStateDto(
                x.Feature.ToString(), x.DisplayName, x.Description, subscription.IsEnabled(x.Feature)))],
            CompareEntitlements(subscription, plan));
    }
}
