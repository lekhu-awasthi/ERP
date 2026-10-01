using ErpApp.Domain.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ErpApp.Infrastructure.Persistence.Configurations.Tenancy;

public sealed class BillingLocationConfiguration : IEntityTypeConfiguration<BillingLocation>
{
    public void Configure(EntityTypeBuilder<BillingLocation> builder)
    {
        builder.ToTable("BillingLocations", schema: "tenancy");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.OrganizationId).IsRequired();
        builder.Property(x => x.Code).HasMaxLength(20).IsRequired();
        builder.Property(x => x.Name).HasMaxLength(100).IsRequired();
        builder.Property(x => x.Address).HasMaxLength(250);
        builder.Property(x => x.LocationType).HasConversion<int>().IsRequired();

        // Phase 60. The SQL default backfills every pre-phase row as None (phase-60-status.md
        // Decision A). None is member 0, so EF's default-sentinel substitution (phase-2 bug #2)
        // would send the same value anyway; ValueGeneratedNever keeps it from ever deciding.
        builder.Property(x => x.PosMode)
            .HasConversion<string>()
            .HasMaxLength(20)
            .IsRequired()
            .HasDefaultValue(PosMode.None)
            .ValueGeneratedNever();
        builder.Property(x => x.IsActive).IsRequired();
        builder.Property(x => x.CreatedAt).IsRequired();

        // Code is the tenant-local identity shown in the CODE column and in the document header's
        // "Name (Code)" label, so it is unique per tenant. Name is deliberately NOT unique: the live
        // tenant's own list carries three rows whose names differ, but nothing in the reference
        // product stops two branches sharing a name, and Warehouse's unique-Name index is a Phase 5
        // convenience rather than a rule to copy here.
        builder.HasIndex(x => new { x.OrganizationId, x.Code }).IsUnique();

        builder.HasIndex(x => x.OrganizationId);
    }
}
