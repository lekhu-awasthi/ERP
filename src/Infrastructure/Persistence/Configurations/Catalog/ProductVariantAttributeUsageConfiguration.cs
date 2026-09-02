using ErpApp.Domain.Catalog;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ErpApp.Infrastructure.Persistence.Configurations.Catalog;

public sealed class ProductVariantAttributeUsageConfiguration : IEntityTypeConfiguration<ProductVariantAttributeUsage>
{
    public void Configure(EntityTypeBuilder<ProductVariantAttributeUsage> builder)
    {
        builder.ToTable("ProductVariantAttributeUsages", schema: "catalog");

        builder.HasKey(x => x.Id);

        // One row per (product, option). The parent-side FK is configured from ProductConfiguration
        // (the owning aggregate); these two are references to the shared attribute catalog, so
        // Restrict -- an attribute a product is using cannot be deleted out from under it.
        builder.HasIndex(x => new { x.ProductId, x.VariantAttributeOptionId }).IsUnique();

        builder.HasOne<VariantAttribute>().WithMany()
            .HasForeignKey(x => x.VariantAttributeId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<VariantAttributeOption>().WithMany()
            .HasForeignKey(x => x.VariantAttributeOptionId).OnDelete(DeleteBehavior.Restrict);
    }
}
