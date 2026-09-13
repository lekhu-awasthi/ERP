using ErpApp.Domain.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ErpApp.Infrastructure.Persistence.Configurations.Tenancy;

public sealed class OrganizationConfiguration : IEntityTypeConfiguration<Organization>
{
    public void Configure(EntityTypeBuilder<Organization> builder)
    {
        builder.ToTable("Organizations", schema: "tenancy");

        builder.HasKey(o => o.Id);

        builder.Property(o => o.Name).HasMaxLength(200).IsRequired();
        builder.Property(o => o.Industry).HasMaxLength(100).IsRequired();
        builder.Property(o => o.Address).HasMaxLength(500);
        builder.Property(o => o.AccountingStartDate).IsRequired();
        builder.Property(o => o.IsVatRegistered).IsRequired();
        builder.Property(o => o.WorkspaceName).HasMaxLength(63).IsRequired();
        builder.Property(o => o.Email).HasMaxLength(256);
        builder.Property(o => o.Phone).HasMaxLength(20);
        builder.Property(o => o.PanNumber).HasMaxLength(50);
        builder.Property(o => o.Website).HasMaxLength(256);

        // Phase 39. Both nullable with no default and no backfill: "this organization has no logo"
        // is the truth about every row that already exists, which is exactly when a column needs
        // neither (phase-37's refinement of phase-31's rule).
        builder.Property(o => o.LogoStorageKey).HasMaxLength(400);
        builder.Property(o => o.LogoContentType).HasMaxLength(100);
        builder.Property(o => o.CreatedByUserId).IsRequired();
        builder.Property(o => o.CreatedAt).IsRequired();
        builder.Property(o => o.RowVersion).IsRowVersion();

        builder.HasIndex(o => o.WorkspaceName).IsUnique();
    }
}
