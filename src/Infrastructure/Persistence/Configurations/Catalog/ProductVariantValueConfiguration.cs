using ErpApp.Domain.Catalog;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ErpApp.Infrastructure.Persistence.Configurations.Catalog;

public sealed class ProductVariantValueConfiguration : IEntityTypeConfiguration<ProductVariantValue>
{
    public void Configure(EntityTypeBuilder<ProductVariantValue> builder)
    {
        builder.ToTable("ProductVariantValues", schema: "catalog");

        builder.HasKey(x => x.Id);

        // A variant takes at most one value per attribute -- the same invariant
        // Product.CreateVariant enforces in memory, made a database rule as well.
        builder.HasIndex(x => new { x.ProductId, x.VariantAttributeId }).IsUnique();

        builder.HasOne<VariantAttribute>().WithMany()
            .HasForeignKey(x => x.VariantAttributeId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<VariantAttributeOption>().WithMany()
            .HasForeignKey(x => x.VariantAttributeOptionId).OnDelete(DeleteBehavior.Restrict);
    }
}
