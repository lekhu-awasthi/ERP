using ErpApp.Domain.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ErpApp.Infrastructure.Persistence.Configurations.Tenancy;

/// <summary>
/// Seeds exactly the two system-level roles Phase 1c calls for (roadmap Phase 1c task 12) --
/// see Role's doc comment for why these are shared across every Organization rather than
/// created per-org.
/// </summary>
public sealed class RoleConfiguration : IEntityTypeConfiguration<Role>
{
    public void Configure(EntityTypeBuilder<Role> builder)
    {
        builder.ToTable("Roles", schema: "tenancy");

        builder.HasKey(r => r.Id);

        builder.Property(r => r.Name).HasMaxLength(50).IsRequired();
        builder.Property(r => r.Description).HasMaxLength(200);

        builder.HasData(
            Role.Create(Role.AdminId, "Admin", "Full access to every permission in the Organization."),
            Role.Create(Role.MemberId, "Member", "Read-only access; cannot invite users or approve join requests."));
    }
}
