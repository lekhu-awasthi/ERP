using ErpApp.Domain.Catalog;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ErpApp.Infrastructure.Persistence.Configurations.Catalog;

public sealed class VariantAttributeConfiguration : IEntityTypeConfiguration<VariantAttribute>
{
    public void Configure(EntityTypeBuilder<VariantAttribute> builder)
    {
        builder.ToTable("VariantAttributes", schema: "catalog");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.OrganizationId).IsRequired();
        builder.Property(x => x.Name).HasMaxLength(100).IsRequired();
        builder.Property(x => x.IsActive).IsRequired();
        builder.Property(x => x.CreatedAt).IsRequired();

        // Deliberately NOT unique. The live reference tenant carries both "size" and "Size", and
        // both "Color" and "color", as separate attributes -- a uniqueness rule here would reject
        // data the product this rebuilds is demonstrably happy with. Duplicate names are a tenant's
        // own housekeeping problem, not an invariant.
        builder.HasIndex(x => new { x.OrganizationId, x.Name });

        builder.HasMany(x => x.Options)
            .WithOne()
            .HasForeignKey(x => x.VariantAttributeId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Metadata.FindNavigation(nameof(VariantAttribute.Options))!
            .SetPropertyAccessMode(PropertyAccessMode.Field);
    }
}
