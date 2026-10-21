using ErpApp.Domain.Sales;
using ErpApp.Domain.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ErpApp.Infrastructure.Persistence.Configurations.Sales;

/// <summary>Phase 97. A scheme's code is unique in its organization, as a document's is.</summary>
public sealed class DiscountSchemeConfiguration : IEntityTypeConfiguration<DiscountScheme>
{
    public void Configure(EntityTypeBuilder<DiscountScheme> builder)
    {
        builder.ToTable("DiscountSchemes", schema: "sales");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.OrganizationId).IsRequired();
        builder.Property(x => x.Code).HasMaxLength(DiscountScheme.CodeMaxLength).IsRequired();
        builder.Property(x => x.Name).HasMaxLength(DiscountScheme.NameMaxLength).IsRequired();
        builder.Property(x => x.Kind).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(x => x.StartDate).IsRequired();
        builder.Property(x => x.EndDate).IsRequired();
        builder.Property(x => x.AllLocations).IsRequired();
        builder.Property(x => x.IsActive).IsRequired();
        builder.Property(x => x.CreatedAt).IsRequired();

        builder.HasIndex(x => new { x.OrganizationId, x.Code }).IsUnique();
    }
}

/// <summary>
/// Phase 97. The product and category a row names carry no foreign key (phase 87 Decision F's shape): a scheme row is a
/// rule about a catalogue entry, read by id, and deleting the entry must not be refused because a promotion once named it.
/// The reader matches only rows whose product or category still exists.
/// </summary>
public sealed class DiscountSchemeLineConfiguration : IEntityTypeConfiguration<DiscountSchemeLine>
{
    public void Configure(EntityTypeBuilder<DiscountSchemeLine> builder)
    {
        builder.ToTable("DiscountSchemeLines", schema: "sales");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.OrganizationId).IsRequired();
        builder.Property(x => x.SortOrder).IsRequired();
        builder.Property(x => x.MinimumAmount).HasPrecision(18, 4);
        builder.Property(x => x.DiscountPct).HasPrecision(18, 4).IsRequired();

        builder.HasIndex(x => new { x.OrganizationId, x.DiscountSchemeId });

        builder.HasOne<DiscountScheme>()
            .WithMany()
            .HasForeignKey(x => x.DiscountSchemeId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

public sealed class DiscountSchemeLocationConfiguration : IEntityTypeConfiguration<DiscountSchemeLocation>
{
    public void Configure(EntityTypeBuilder<DiscountSchemeLocation> builder)
    {
        builder.ToTable("DiscountSchemeLocations", schema: "sales");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.OrganizationId).IsRequired();

        builder.HasIndex(x => new { x.OrganizationId, x.DiscountSchemeId, x.BillingLocationId }).IsUnique();

        builder.HasOne<DiscountScheme>()
            .WithMany()
            .HasForeignKey(x => x.DiscountSchemeId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne<BillingLocation>()
            .WithMany()
            .HasForeignKey(x => x.BillingLocationId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
