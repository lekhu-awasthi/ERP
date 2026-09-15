using System.Reflection;
using ErpApp.Application.Common.Behaviors;
using ErpApp.Application.Common.Exceptions;
using ErpApp.Application.Common.Persistence;
using ErpApp.Application.Common.Security;
using ErpApp.Application.Tenancy.Commands.CreateWarehouse;
using ErpApp.Application.Tenancy.Commands.SetTenantSubscription;
using ErpApp.Application.UnitTests.TestSupport;
using ErpApp.Domain.Tenancy;

namespace ErpApp.Application.UnitTests.Tenancy;

/// <summary>
/// Phase 46 (phase 31 carried item #6) -- master data stops being writable once the term has ended,
/// and the escape hatches phase 31 argued for stay open.
/// </summary>
public class ExpiredTenantMasterDataTests
{
    private static readonly Guid OrganizationId = Guid.NewGuid();

    /// <summary>
    /// The set, listed rather than derived, because this one is a <b>curated</b> boundary and not a
    /// catalogue: phase 34b's rule. Deriving it from "every Create/Update command" would sweep in
    /// every settings command too, which is precisely the line phase 31 drew and this phase kept.
    /// </summary>
    private static readonly string[] ExpectedMasterDataCommands =
    [
        "CreateAccountCommand",
        "CreateAccountGroupCommand",
        "CreateBillingLocationCommand",
        "CreateContactCommand",
        "CreateProductCommand",
        "CreateWarehouseCommand",
        "UpdateAccountCommand",
        "UpdateAccountGroupCommand",
        "UpdateBillingLocationCommand",
        "UpdateContactCommand",
        "UpdateProductCommand",
        "UpdateWarehouseCommand",
    ];

    [Fact]
    public void The_expiry_gate_covers_exactly_the_intended_master_data_commands()
    {
        var marked = typeof(IExpirySensitiveMasterData).Assembly
            .GetTypes()
            .Where(t => t is { IsAbstract: false, IsInterface: false }
                && typeof(IExpirySensitiveMasterData).IsAssignableFrom(t))
            .Select(t => t.Name)
            .OrderBy(x => x, StringComparer.Ordinal)
            .ToList();

        Assert.Equal(ExpectedMasterDataCommands, marked);
    }

    /// <summary>
    /// Phase 12's rule, applied to the new marker: the behavior resolves the tenant through
    /// <c>IOrganizationScoped</c>, so a marked command without it would sail past the gate and the
    /// gate would look like it was working.
    /// </summary>
    [Fact]
    public void Every_expiry_sensitive_command_is_organization_scoped()
    {
        var marked = typeof(IExpirySensitiveMasterData).Assembly
            .GetTypes()
            .Where(t => t is { IsAbstract: false, IsInterface: false }
                && typeof(IExpirySensitiveMasterData).IsAssignableFrom(t));

        Assert.All(marked, t => Assert.True(
            typeof(IOrganizationScoped).IsAssignableFrom(t),
            $"{t.Name} is expiry-sensitive but not IOrganizationScoped, so the gate cannot resolve its tenant."));
    }

    /// <summary>
    /// The three marker sets are deliberately distinct, not one merged set. A master-data command
    /// must not become lock-date-sensitive by accident: a lock date closes an accounting period, and
    /// a product does not sit in a period.
    /// </summary>
    [Fact]
    public void Master_data_is_not_lock_date_sensitive()
    {
        var marked = typeof(IExpirySensitiveMasterData).Assembly
            .GetTypes()
            .Where(t => t is { IsAbstract: false, IsInterface: false }
                && typeof(IExpirySensitiveMasterData).IsAssignableFrom(t));

        Assert.All(marked, t =>
        {
            Assert.False(typeof(ILockDateSensitive).IsAssignableFrom(t), t.Name);
            Assert.False(typeof(ILockDateSensitiveDocument).IsAssignableFrom(t), t.Name);
        });
    }

    [Fact]
    public async Task An_expired_tenant_cannot_create_master_data()
    {
        var db = TestAppDbContext.Create();
        await SeedExpiredAsync(db);

        var behavior = new SubscriptionExpiryBehavior<CreateWarehouseCommand, CreateWarehouseResult>(db);

        var exception = await Assert.ThrowsAsync<ConflictException>(() => behavior.Handle(
            new CreateWarehouseCommand(OrganizationId, "Main Warehouse"),
            () => Task.FromResult(new CreateWarehouseResult(Guid.NewGuid(), "Main Warehouse")),
            CancellationToken.None));

        Assert.Contains("read-only", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_live_tenant_can_create_master_data()
    {
        var db = TestAppDbContext.Create();
        await SeedLiveAsync(db);

        var behavior = new SubscriptionExpiryBehavior<CreateWarehouseCommand, CreateWarehouseResult>(db);
        var expected = new CreateWarehouseResult(Guid.NewGuid(), "Main Warehouse");

        var actual = await behavior.Handle(
            new CreateWarehouseCommand(OrganizationId, "Main Warehouse"),
            () => Task.FromResult(expected),
            CancellationToken.None);

        Assert.Same(expected, actual);
    }

    /// <summary>
    /// Phase 31's escape hatch, and the reason this phase narrowed its item rather than reversing it:
    /// the command that lifts the expiry must stay reachable from inside the expired tenant, or
    /// nobody can ever renew.
    /// </summary>
    [Fact]
    public async Task An_expired_tenant_can_still_record_a_new_term()
    {
        var db = TestAppDbContext.Create();
        await SeedExpiredAsync(db);

        var behavior = new SubscriptionExpiryBehavior<SetTenantSubscriptionCommand, Application.Tenancy.Queries.GetTenantSubscription.TenantSubscriptionDto>(db);
        var reached = false;

        await behavior.Handle(
            new SetTenantSubscriptionCommand(OrganizationId, null, DateTimeOffset.UtcNow.AddDays(30)),
            () =>
            {
                reached = true;
                return Task.FromResult<Application.Tenancy.Queries.GetTenantSubscription.TenantSubscriptionDto>(null!);
            },
            CancellationToken.None);

        Assert.True(reached);
    }

    private static Task SeedExpiredAsync(IAppDbContext db) => SeedAsync(db, DateTimeOffset.UtcNow.AddDays(-1));

    private static Task SeedLiveAsync(IAppDbContext db) => SeedAsync(db, DateTimeOffset.UtcNow.AddDays(30));

    /// <summary>
    /// Reaches an expired state through the change tracker rather than by weakening the Domain --
    /// phase 31's rule: <c>SetPlan</c> refuses a term that has already ended, and it should, because
    /// nothing should be able to record one.
    /// </summary>
    private static async Task SeedAsync(IAppDbContext db, DateTimeOffset termEndsAt)
    {
        var subscription = TenantSubscription.CreateTrial(OrganizationId, default);
        db.TenantSubscriptions.Add(subscription);
        await db.SaveChangesAsync(CancellationToken.None);

        db.TenantSubscriptions.Entry(subscription)
            .Property(nameof(TenantSubscription.TermEndsAt)).CurrentValue = termEndsAt;
        await db.SaveChangesAsync(CancellationToken.None);
    }
}
