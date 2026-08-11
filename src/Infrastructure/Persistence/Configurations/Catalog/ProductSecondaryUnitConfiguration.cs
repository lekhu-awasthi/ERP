using ErpApp.Domain.Catalog;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ErpApp.Infrastructure.Persistence.Configurations.Catalog;

public sealed class ProductSecondaryUnitConfiguration : IEntityTypeConfiguration<ProductSecondaryUnit>
{
    public void Configure(EntityTypeBuilder<ProductSecondaryUnit> builder)
    {
        builder.ToTable("ProductSecondaryUnits", schema: "catalog");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.ConversionRate).HasPrecision(18, 6).IsRequired();
        builder.Property(x => x.SellingPrice).HasPrecision(18, 4).IsRequired();
        builder.Property(x => x.PurchasePrice).HasPrecision(18, 4).IsRequired();

        builder.HasIndex(x => new { x.ProductId, x.UnitId }).IsUnique();

        builder.HasOne<UnitOfMeasurement>()
            .WithMany()
            .HasForeignKey(x => x.UnitId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
