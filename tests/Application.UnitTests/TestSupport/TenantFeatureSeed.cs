using ErpApp.Application.Common.Persistence;
using ErpApp.Domain.Tenancy;

namespace ErpApp.Application.UnitTests.TestSupport;

/// <summary>
/// Phase 20f (FR-2.6). Seeds the TenantSubscription row a feature-gated code path needs, through
/// the real <see cref="TenantSubscription.CreateTrial"/> factory rather than by hand-constructing
/// the entity -- the same path CreateOrganizationCommandHandler takes, so a test can't accidentally
/// depend on a flag combination the real wizard could never produce.
///
/// Most handler tests in this project use a bare Guid for OrganizationId and never create an
/// Organization row at all, so they have no subscription either; only the paths that actually
/// consult one (today: CreateWarehouseCommandHandler's multiple-warehouse cap, and anything run
/// through FeatureGateBehavior) need this.
/// </summary>
public static class TenantFeatureSeed
{
    /// <summary>Every Accounting Feature opted in -- the default for a test whose subject isn't the
    /// gate itself and which just needs the gate to get out of the way.</summary>
    public static async Task SeedAllFeaturesEnabledAsync(IAppDbContext db, Guid organizationId)
    {
        await SeedAsync(db, organizationId, new AccountingFeatureSelections(
            TrackInventory: true,
            MultipleLocations: true,
            MultipleWarehouses: true,
            MultiCurrency: true,
            Manufacturing: true,
            PosRetail: true,
            PosRestaurant: true));
    }

    public static async Task SeedAsync(IAppDbContext db, Guid organizationId, AccountingFeatureSelections features)
    {
        db.TenantSubscriptions.Add(TenantSubscription.CreateTrial(organizationId, features));
        await db.SaveChangesAsync(CancellationToken.None);
    }
}
