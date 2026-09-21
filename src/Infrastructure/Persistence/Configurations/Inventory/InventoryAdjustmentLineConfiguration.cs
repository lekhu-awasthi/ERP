using ErpApp.Domain.Catalog;
using ErpApp.Domain.Common;
using ErpApp.Domain.Inventory;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ErpApp.Infrastructure.Persistence.Configurations.Inventory;

public sealed class InventoryAdjustmentLineConfiguration : IEntityTypeConfiguration<InventoryAdjustmentLine>
{
    public void Configure(EntityTypeBuilder<InventoryAdjustmentLine> builder)
    {
        builder.ToTable("InventoryAdjustmentLines", schema: "inventory");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Direction).HasConversion<string>().HasMaxLength(10).IsRequired();
        builder.Property(x => x.Quantity).HasPrecision(18, 4).IsRequired();
        builder.Property(x => x.UnitCost).HasPrecision(18, 4).IsRequired();
        builder.Property(x => x.ConsumedUnitCost).HasPrecision(18, 4);

        builder.HasOne<Product>().WithMany().HasForeignKey(x => x.ProductId).OnDelete(DeleteBehavior.Restrict);

        // Phase 54 -- the unit this line was entered in, and the factor frozen with it. Same shape
        // as the eight phase-52 line types: Restrict on the unit lookup, because deleting a unit a
        // document line names would rewrite an approved document's history. It does NOT restrict
        // deleting the product's own secondary-unit row -- the reference product allows exactly
        // that, and nothing here breaks, because the line names the unit lookup and carries its own
        // factor.
        builder.Property(x => x.ConversionFactor)
            .HasPrecision(18, UnitConversion.FactorScale)
            .HasDefaultValue(UnitConversion.PrimaryFactor)
            .IsRequired();

        builder.HasOne<UnitOfMeasurement>().WithMany().HasForeignKey(x => x.UnitId)
            .OnDelete(DeleteBehavior.Restrict);

        // Derived from Quantity and ConversionFactor, both on this row.
        builder.Ignore(x => x.PrimaryQuantity);
    }
}
