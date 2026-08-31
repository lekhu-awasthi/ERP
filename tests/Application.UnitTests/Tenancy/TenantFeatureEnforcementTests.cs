using ErpApp.Application.Common.Exceptions;
using ErpApp.Application.Tenancy.Commands.CreateOrganization;
using ErpApp.Application.Tenancy.Commands.CreateWarehouse;
using ErpApp.Application.Tenancy.Queries.GetTenantSubscription;
using ErpApp.Application.UnitTests.TestSupport;
using ErpApp.Domain.Tenancy;

namespace ErpApp.Application.UnitTests.Tenancy;

/// <summary>
/// Phase 20f (FR-2.6). Two things FeatureGateBehavior can't cover on its own: the
/// MultipleWarehouses cap (a *conditional* gate, so it lives in CreateWarehouseCommandHandler --
/// see that method's doc comment) and the read-only subscription query the Angular shell reads to
/// decide which feature-gated nav entries to render.
///
/// Both seed through the real CreateOrganizationCommandHandler rather than hand-inserting a
/// TenantSubscription row, so these exercise the actual path a tenant takes.
/// </summary>
public class TenantFeatureEnforcementTests
{
    private static CreateOrganizationCommand OrganizationCommand(string workspace, bool multipleWarehouses)
    {
        return new CreateOrganizationCommand(
            $"Org {workspace}", "Retail", null, new DateOnly(2026, 1, 1), true, workspace, null, null, null, null,
            TrackInventory: true,
            MultipleLocations: false,
            MultipleWarehouses: multipleWarehouses,
            MultiCurrency: false,
            Manufacturing: false,
            PosRetail: false,
            PosRestaurant: false);
    }

    [Fact]
    public async Task CreateWarehouse_allows_the_first_warehouse_even_without_the_entitlement()
    {
        // The load-bearing case: Invoice and PurchaseBill both require a WarehouseId and nothing
        // seeds a default warehouse at Organization creation, so a tenant who skipped the
        // checkbox must still be able to create exactly one -- otherwise they could never invoice.
        var db = TestAppDbContext.Create();
        var currentUser = new FakeCurrentUserService(Guid.NewGuid());
        var organization = await new CreateOrganizationCommandHandler(db, currentUser).Handle(
            OrganizationCommand("single-warehouse-org", multipleWarehouses: false), CancellationToken.None);

        var warehouse = await new CreateWarehouseCommandHandler(db).Handle(
            new CreateWarehouseCommand(organization.OrganizationId, "Main Warehouse"), CancellationToken.None);

        Assert.Equal("Main Warehouse", warehouse.Name);
    }

    [Fact]
    public async Task CreateWarehouse_rejects_the_second_warehouse_without_the_entitlement()
    {
        var db = TestAppDbContext.Create();
        var currentUser = new FakeCurrentUserService(Guid.NewGuid());
        var organization = await new CreateOrganizationCommandHandler(db, currentUser).Handle(
            OrganizationCommand("capped-org", multipleWarehouses: false), CancellationToken.None);

        await new CreateWarehouseCommandHandler(db).Handle(
            new CreateWarehouseCommand(organization.OrganizationId, "Main Warehouse"), CancellationToken.None);

        var exception = await Assert.ThrowsAsync<FeatureNotEnabledException>(() =>
            new CreateWarehouseCommandHandler(db).Handle(
                new CreateWarehouseCommand(organization.OrganizationId, "Second Warehouse"), CancellationToken.None));

        Assert.Contains("Multiple Warehouses", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CreateWarehouse_allows_the_second_warehouse_with_the_entitlement()
    {
        var db = TestAppDbContext.Create();
        var currentUser = new FakeCurrentUserService(Guid.NewGuid());
        var organization = await new CreateOrganizationCommandHandler(db, currentUser).Handle(
            OrganizationCommand("multi-warehouse-org", multipleWarehouses: true), CancellationToken.None);

        await new CreateWarehouseCommandHandler(db).Handle(
            new CreateWarehouseCommand(organization.OrganizationId, "Main Warehouse"), CancellationToken.None);
        var second = await new CreateWarehouseCommandHandler(db).Handle(
            new CreateWarehouseCommand(organization.OrganizationId, "Second Warehouse"), CancellationToken.None);

        Assert.Equal("Second Warehouse", second.Name);
    }

    [Fact]
    public async Task CreateWarehouse_caps_each_organization_independently()
    {
        var db = TestAppDbContext.Create();
        var currentUser = new FakeCurrentUserService(Guid.NewGuid());
        var capped = await new CreateOrganizationCommandHandler(db, currentUser).Handle(
            OrganizationCommand("org-a", multipleWarehouses: false), CancellationToken.None);
        var uncapped = await new CreateOrganizationCommandHandler(db, currentUser).Handle(
            OrganizationCommand("org-b", multipleWarehouses: true), CancellationToken.None);

        foreach (var organizationId in new[] { capped.OrganizationId, uncapped.OrganizationId })
        {
            await new CreateWarehouseCommandHandler(db).Handle(
                new CreateWarehouseCommand(organizationId, "Main Warehouse"), CancellationToken.None);
        }

        await Assert.ThrowsAsync<FeatureNotEnabledException>(() => new CreateWarehouseCommandHandler(db).Handle(
            new CreateWarehouseCommand(capped.OrganizationId, "Second Warehouse"), CancellationToken.None));

        var second = await new CreateWarehouseCommandHandler(db).Handle(
            new CreateWarehouseCommand(uncapped.OrganizationId, "Second Warehouse"), CancellationToken.None);

        Assert.Equal("Second Warehouse", second.Name);
    }

    [Fact]
    public async Task GetTenantSubscription_returns_every_feature_with_the_state_chosen_at_creation()
    {
        var db = TestAppDbContext.Create();
        var currentUser = new FakeCurrentUserService(Guid.NewGuid());
        var organization = await new CreateOrganizationCommandHandler(db, currentUser).Handle(
            OrganizationCommand("subscription-org", multipleWarehouses: true), CancellationToken.None);

        var result = await new GetTenantSubscriptionQueryHandler(db).Handle(
            new GetTenantSubscriptionQuery(organization.OrganizationId), CancellationToken.None);

        Assert.Equal("Trial", result.PlanName);
        Assert.True(result.IsTrialActive);
        Assert.Equal(7, result.Features.Count);

        // One row per TenantFeature member, in enum order, so the screen can't silently drop one.
        Assert.Equal(
            Enum.GetValues<TenantFeature>().Select(x => x.ToString()),
            result.Features.Select(x => x.Feature));

        Assert.True(result.Features.Single(x => x.Feature == nameof(TenantFeature.TrackInventory)).IsEnabled);
        Assert.True(result.Features.Single(x => x.Feature == nameof(TenantFeature.MultipleWarehouses)).IsEnabled);
        Assert.False(result.Features.Single(x => x.Feature == nameof(TenantFeature.MultiCurrency)).IsEnabled);
        Assert.False(result.Features.Single(x => x.Feature == nameof(TenantFeature.Manufacturing)).IsEnabled);
        Assert.All(result.Features, x => Assert.False(string.IsNullOrWhiteSpace(x.Description)));
    }

    [Fact]
    public async Task GetTenantSubscription_throws_not_found_for_an_organization_without_a_subscription()
    {
        var db = TestAppDbContext.Create();

        await Assert.ThrowsAsync<NotFoundException>(() => new GetTenantSubscriptionQueryHandler(db).Handle(
            new GetTenantSubscriptionQuery(Guid.NewGuid()), CancellationToken.None));
    }
}
