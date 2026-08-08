using System.Reflection;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Infrastructure.Persistence;

/// <summary>
/// Phase 0: intentionally empty of DbSets. Its only job right now is to prove EF Core
/// can connect to SQL Server and apply migrations. Each bounded context will add its
/// own IEntityTypeConfiguration&lt;T&gt; under Persistence/Configurations/&lt;Context&gt;/
/// starting in Phase 1 (Identity/Tenancy), at which point ApplyConfigurationsFromAssembly
/// below will start picking them up automatically.
/// </summary>
public sealed class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.ApplyConfigurationsFromAssembly(Assembly.GetExecutingAssembly());
    }
}
