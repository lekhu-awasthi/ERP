using ErpApp.Domain.Identity;
using ErpApp.Domain.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.Common.Persistence;

/// <summary>
/// Application-layer view of the EF Core DbContext. Keeps command/query handlers dependent
/// on this interface (Domain-shaped, DbSet&lt;T&gt; abstraction) rather than on
/// ErpApp.Infrastructure.Persistence.AppDbContext directly, preserving the
/// Api -> Application -> Domain dependency rule (architecture-spec.md §1).
/// </summary>
public interface IAppDbContext
{
    DbSet<User> Users { get; }
    DbSet<VerificationCode> VerificationCodes { get; }
    DbSet<Organization> Organizations { get; }
    DbSet<TenantSettings> TenantSettings { get; }
    DbSet<TenantSubscription> TenantSubscriptions { get; }
    DbSet<OrganizationMembership> OrganizationMemberships { get; }

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
