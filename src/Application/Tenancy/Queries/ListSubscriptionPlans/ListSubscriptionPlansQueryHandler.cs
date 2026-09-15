using ErpApp.Application.Common.Persistence;
using ErpApp.Domain.Tenancy;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.Tenancy.Queries.ListSubscriptionPlans;

public sealed class ListSubscriptionPlansQueryHandler(IAppDbContext db)
    : IRequestHandler<ListSubscriptionPlansQuery, IReadOnlyList<SubscriptionPlanDto>>
{
    public async Task<IReadOnlyList<SubscriptionPlanDto>> Handle(
        ListSubscriptionPlansQuery request, CancellationToken cancellationToken)
    {
        var plans = await db.SubscriptionPlans
            .AsNoTracking()
            .OrderBy(x => x.DisplayOrder)
            .ToListAsync(cancellationToken);

        return [.. plans.Select(ToDto)];
    }

    /// <summary>
    /// The feature rows, in the published price list's own order and wording, so a tenant comparing
    /// tiers in the app sees the same list it saw on the website. Built here rather than stored as a
    /// projection so the labels are one string apiece and adding a row is not a migration.
    /// </summary>
    internal static SubscriptionPlanDto ToDto(SubscriptionPlan plan)
    {
        return new SubscriptionPlanDto(
            plan.Id,
            plan.Code,
            plan.Name,
            plan.Description,
            plan.AnnualAmount,
            plan.ProductQuota,
            plan.TransactionQuota,
            plan.DailyAiScanQuota,
            [
                new SubscriptionPlanFeatureDto("Multiple currency", plan.MultiCurrencyIncluded),
                new SubscriptionPlanFeatureDto("Inventory tracking", plan.TrackInventoryIncluded),
                new SubscriptionPlanFeatureDto("Multiple warehouses", plan.MultipleWarehousesIncluded),
                new SubscriptionPlanFeatureDto("Landed cost calculation", plan.LandedCostIncluded),
                new SubscriptionPlanFeatureDto("Production feature", plan.ManufacturingIncluded),
                new SubscriptionPlanFeatureDto("POS (Retail/Restro)", plan.PosIncluded),
                new SubscriptionPlanFeatureDto("Developer API", plan.DeveloperApiIncluded),
            ]);
    }
}
