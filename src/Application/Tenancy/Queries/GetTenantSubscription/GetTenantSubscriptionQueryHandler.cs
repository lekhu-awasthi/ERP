using ErpApp.Application.Common.Exceptions;
using ErpApp.Application.Common.Persistence;
using ErpApp.Domain.Tenancy;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.Tenancy.Queries.GetTenantSubscription;

public sealed class GetTenantSubscriptionQueryHandler(IAppDbContext db)
    : IRequestHandler<GetTenantSubscriptionQuery, TenantSubscriptionDto>
{
    /// <summary>
    /// Display name + description per feature, taken verbatim from the New Organization wizard's
    /// own Step 2 checkbox cards, so the read-only Features screen names each entitlement exactly
    /// the way the user saw it when they chose (or skipped) it at creation.
    /// </summary>
    private static readonly (TenantFeature Feature, string DisplayName, string Description)[] Catalog =
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

    public async Task<TenantSubscriptionDto> Handle(
        GetTenantSubscriptionQuery request, CancellationToken cancellationToken)
    {
        var subscription = await db.TenantSubscriptions.SingleOrDefaultAsync(
            x => x.OrganizationId == request.OrganizationId, cancellationToken)
            ?? throw new NotFoundException("This organization has no subscription record.");

        var now = DateTimeOffset.UtcNow;
        var daysRemaining = (int)Math.Ceiling((subscription.TrialEndsAt - now).TotalDays);

        return new TenantSubscriptionDto(
            subscription.OrganizationId,
            subscription.PlanName,
            subscription.TrialStartsAt,
            subscription.TrialEndsAt,
            subscription.TrialEndsAt > now,
            Math.Max(daysRemaining, 0),
            subscription.IrdSyncEnabled,
            [.. Catalog.Select(x => new TenantFeatureStateDto(
                x.Feature.ToString(), x.DisplayName, x.Description, subscription.IsEnabled(x.Feature)))]);
    }
}
