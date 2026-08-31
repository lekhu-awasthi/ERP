using ErpApp.Application.Common.Behaviors;
using ErpApp.Application.Common.Exceptions;
using ErpApp.Application.Common.Security;
using ErpApp.Application.Inventory.Queries.ProductStockPosition;
using ErpApp.Application.Sales.Commands.CreateInvoice;
using ErpApp.Application.UnitTests.TestSupport;
using ErpApp.Domain.Tenancy;
using MediatR;

namespace ErpApp.Application.UnitTests.Common.Behaviors;

/// <summary>
/// Covers tenant feature-flag enforcement (roadmap Phase 20f, FR-2.6) -- the pipeline behavior
/// that finally makes TenantSubscription's entitlement flags a real gate rather than ambient
/// state written once at Organization creation and read nowhere.
///
/// ProductStockPositionQuery (Track Inventory) and CreateWarehouseTransferCommand (Track
/// Inventory + Multiple Warehouses, the only two-feature request in this codebase) stand in for
/// the whole gated set; CreateInvoiceCommand stands in for the far larger ungated set.
/// </summary>
public class FeatureGateBehaviorTests
{
    private static readonly RequestHandlerDelegate<IReadOnlyList<StockPositionDto>> NextMustNotRun =
        () => throw new InvalidOperationException("next() should not have been called.");

    [Fact]
    public async Task Handle_allows_a_gated_request_when_the_tenant_opted_into_the_feature()
    {
        var db = TestAppDbContext.Create();
        var organizationId = Guid.NewGuid();
        await TenantFeatureSeed.SeedAsync(db, organizationId, new AccountingFeatureSelections(
            TrackInventory: true, MultipleLocations: false, MultipleWarehouses: false,
            MultiCurrency: false, Manufacturing: false, PosRetail: false, PosRestaurant: false));

        var behavior = new FeatureGateBehavior<ProductStockPositionQuery, IReadOnlyList<StockPositionDto>>(db);
        var reached = false;

        var result = await behavior.Handle(
            new ProductStockPositionQuery(organizationId, null, null),
            () =>
            {
                reached = true;
                return Task.FromResult<IReadOnlyList<StockPositionDto>>([]);
            },
            CancellationToken.None);

        Assert.True(reached);
        Assert.Empty(result);
    }

    [Fact]
    public async Task Handle_throws_naming_the_feature_when_the_tenant_did_not_opt_in()
    {
        var db = TestAppDbContext.Create();
        var organizationId = Guid.NewGuid();
        await TenantFeatureSeed.SeedAsync(db, organizationId, default);

        var behavior = new FeatureGateBehavior<ProductStockPositionQuery, IReadOnlyList<StockPositionDto>>(db);

        var exception = await Assert.ThrowsAsync<FeatureNotEnabledException>(() => behavior.Handle(
            new ProductStockPositionQuery(organizationId, null, null), NextMustNotRun, CancellationToken.None));

        Assert.Contains("Track Inventory", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Handle_fails_closed_when_the_organization_has_no_subscription_row()
    {
        var db = TestAppDbContext.Create();

        var behavior = new FeatureGateBehavior<ProductStockPositionQuery, IReadOnlyList<StockPositionDto>>(db);

        await Assert.ThrowsAsync<FeatureNotEnabledException>(() => behavior.Handle(
            new ProductStockPositionQuery(Guid.NewGuid(), null, null), NextMustNotRun, CancellationToken.None));
    }

    [Fact]
    public async Task Handle_reads_the_flags_of_the_requests_own_organization_only()
    {
        var db = TestAppDbContext.Create();
        var enabledOrganizationId = Guid.NewGuid();
        var disabledOrganizationId = Guid.NewGuid();
        await TenantFeatureSeed.SeedAllFeaturesEnabledAsync(db, enabledOrganizationId);
        await TenantFeatureSeed.SeedAsync(db, disabledOrganizationId, default);

        var behavior = new FeatureGateBehavior<ProductStockPositionQuery, IReadOnlyList<StockPositionDto>>(db);

        await Assert.ThrowsAsync<FeatureNotEnabledException>(() => behavior.Handle(
            new ProductStockPositionQuery(disabledOrganizationId, null, null), NextMustNotRun, CancellationToken.None));
    }

    [Fact]
    public async Task Handle_skips_a_request_that_does_not_declare_any_feature()
    {
        var db = TestAppDbContext.Create();
        var reached = false;

        // No subscription row at all for this organization -- an ungated request must not care.
        var behavior = new FeatureGateBehavior<CreateInvoiceCommand, CreateInvoiceResult>(db);

        await behavior.Handle(
            new CreateInvoiceCommand(
                Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), new DateOnly(2026, 5, 1), null, []),
            () =>
            {
                reached = true;
                return Task.FromResult(new CreateInvoiceResult(Guid.NewGuid(), "DRAFT", default));
            },
            CancellationToken.None);

        Assert.True(reached);
    }

    [Fact]
    public async Task Handle_requires_every_declared_feature_not_just_the_first()
    {
        var db = TestAppDbContext.Create();
        var organizationId = Guid.NewGuid();

        // Track Inventory on, Multiple Warehouses off -- a WarehouseTransfer needs both, so the
        // second feature is what must reject it. A behavior that stopped at the first satisfied
        // feature would let this through.
        await TenantFeatureSeed.SeedAsync(db, organizationId, new AccountingFeatureSelections(
            TrackInventory: true, MultipleLocations: false, MultipleWarehouses: false,
            MultiCurrency: false, Manufacturing: false, PosRetail: false, PosRestaurant: false));

        var behavior = new FeatureGateBehavior<
            Application.Inventory.Commands.CreateWarehouseTransfer.CreateWarehouseTransferCommand,
            Application.Inventory.Commands.CreateWarehouseTransfer.CreateWarehouseTransferResult>(db);

        var exception = await Assert.ThrowsAsync<FeatureNotEnabledException>(() => behavior.Handle(
            new Application.Inventory.Commands.CreateWarehouseTransfer.CreateWarehouseTransferCommand(
                organizationId, Guid.NewGuid(), Guid.NewGuid(), new DateOnly(2026, 5, 1), null, []),
            () => throw new InvalidOperationException("next() should not have been called."),
            CancellationToken.None));

        Assert.Contains("Multiple Warehouses", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Handle_throws_for_a_feature_gated_request_that_is_not_organization_scoped()
    {
        var db = TestAppDbContext.Create();
        var behavior = new FeatureGateBehavior<UnscopedFeatureRequest, Unit>(db);

        // A wiring bug, not a tenant condition: a silent skip here is exactly the failure mode
        // phase-12 hit, so the behavior must be loud about it.
        await Assert.ThrowsAsync<InvalidOperationException>(() => behavior.Handle(
            new UnscopedFeatureRequest(), () => Task.FromResult(Unit.Value), CancellationToken.None));
    }

    private sealed record UnscopedFeatureRequest : IRequest<Unit>, IRequireFeature
    {
        public IReadOnlyCollection<TenantFeature> RequiredFeatures => [TenantFeature.TrackInventory];
    }
}
