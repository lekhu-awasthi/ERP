using ErpApp.Domain.Catalog;
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
    }
}
