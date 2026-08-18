using ErpApp.Domain.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ErpApp.Infrastructure.Persistence.Configurations.Tenancy;

/// <summary>
/// Seeds the two system-level roles Phase 1c calls for (roadmap Phase 1c task 12) -- see Role's
/// doc comment for why these stay shared across every Organization (OrganizationId null) rather
/// than being created per-org. Phase 14 (Role Reference) adds real per-tenant custom roles on top
/// of this same table, created through CreateRoleCommand rather than HasData.
/// </summary>
public sealed class RoleConfiguration : IEntityTypeConfiguration<Role>
{
    public void Configure(EntityTypeBuilder<Role> builder)
    {
        builder.ToTable("Roles", schema: "tenancy");

        builder.HasKey(r => r.Id);

        builder.Property(r => r.OrganizationId);
        builder.Property(r => r.Name).HasMaxLength(50).IsRequired();
        builder.Property(r => r.Description).HasMaxLength(200);

        // Nullable OrganizationId means SQL Server treats every system role's (null, Name) pair as
        // distinct for uniqueness purposes -- fine here since the two seeded system rows already
        // have distinct Names; this index only actually enforces uniqueness within one tenant's
        // own custom roles.
        builder.HasIndex(r => new { r.OrganizationId, r.Name }).IsUnique();

        builder.HasData(
            Role.Create(Role.AdminId, "Admin", "Full access to every permission in the Organization."),
            Role.Create(Role.MemberId, "Member", "Read-only access; cannot invite users or approve join requests."));
    }
}
