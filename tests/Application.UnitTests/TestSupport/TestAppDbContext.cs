using ErpApp.Application.Common.Persistence;
using ErpApp.Domain.Identity;
using ErpApp.Domain.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.UnitTests.TestSupport;

/// <summary>EF Core InMemory-backed IAppDbContext for handler tests, avoiding a SQL Server dependency.</summary>
public sealed class TestAppDbContext(DbContextOptions<TestAppDbContext> options) : DbContext(options), IAppDbContext
{
    public DbSet<User> Users => Set<User>();

    public DbSet<VerificationCode> VerificationCodes => Set<VerificationCode>();

    public DbSet<Organization> Organizations => Set<Organization>();

    public DbSet<TenantSettings> TenantSettings => Set<TenantSettings>();

    public DbSet<TenantSubscription> TenantSubscriptions => Set<TenantSubscription>();

    public DbSet<OrganizationMembership> OrganizationMemberships => Set<OrganizationMembership>();

    public DbSet<Role> Roles => Set<Role>();

    public DbSet<RolePermission> RolePermissions => Set<RolePermission>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // RowVersion is a SQL Server-generated concurrency token (rowversion column) the real
        // AppDbContext maps via IsRowVersion(); the InMemory provider has no equivalent
        // store-generation, so it's excluded from this test-only model entirely.
        modelBuilder.Entity<User>().Ignore(u => u.RowVersion);
        modelBuilder.Entity<Organization>().Ignore(o => o.RowVersion);
    }

    public static IAppDbContext Create() => new TestAppDbContext(
        new DbContextOptionsBuilder<TestAppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);
}
