using ErpApp.Domain.Catalog;
using ErpApp.Domain.Inventory;
using ErpApp.Domain.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ErpApp.Infrastructure.Persistence.Configurations.Inventory;

public sealed class StockMovementConfiguration : IEntityTypeConfiguration<StockMovement>
{
    public void Configure(EntityTypeBuilder<StockMovement> builder)
    {
        builder.ToTable("StockMovements", schema: "inventory");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.OrganizationId).IsRequired();
        builder.Property(x => x.Direction).HasConversion<string>().HasMaxLength(10).IsRequired();
        builder.Property(x => x.Quantity).HasPrecision(18, 4).IsRequired();
        builder.Property(x => x.UnitCost).HasPrecision(18, 4).IsRequired();
        builder.Property(x => x.SourceDocumentType).HasConversion<string>().HasMaxLength(30).IsRequired();
        builder.Property(x => x.SourceDocumentId).IsRequired();
        builder.Property(x => x.TransactionDate).IsRequired();
        builder.Property(x => x.CreatedAt).IsRequired();

        builder.HasOne<Product>().WithMany().HasForeignKey(x => x.ProductId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Warehouse>().WithMany().HasForeignKey(x => x.WarehouseId).OnDelete(DeleteBehavior.Restrict);

        // InventoryLedgerQuery's own query shape: every movement for one (org, product,
        // warehouse), chronological.
        builder.HasIndex(x => new { x.OrganizationId, x.ProductId, x.WarehouseId, x.TransactionDate });
    }
}
