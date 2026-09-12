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
        // Phase 37 -- the value-only cost catch-up; zero on every quantity-bearing row. Two
        // decimals, not four: it is a ledger amount (it is posted to the Inventory account by
        // StockCostCatchUp), not a unit cost.
        builder.Property(x => x.ValueAdjustment).HasPrecision(18, 2).IsRequired().HasDefaultValue(0m);
        builder.Property(x => x.SourceDocumentType).HasConversion<string>().HasMaxLength(30).IsRequired();
        builder.Property(x => x.SourceDocumentId).IsRequired();
        builder.Property(x => x.TransactionDate).IsRequired();
        builder.Property(x => x.CreatedAt).IsRequired();
        // Phase 35b -- nullable, and un-indexed for the same reason as GlJournalEntry.LocationId:
        // every stock report applies this alongside its period and its product/warehouse narrowing,
        // so the seek is already the (OrganizationId, ProductId, WarehouseId, TransactionDate) index
        // below and the location is a residual predicate over what that has narrowed. Phase 34c
        // measured what a second index over the same table does to the paths that do not use it.
        builder.Property(x => x.LocationId);

        builder.HasOne<Product>().WithMany().HasForeignKey(x => x.ProductId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Warehouse>().WithMany().HasForeignKey(x => x.WarehouseId).OnDelete(DeleteBehavior.Restrict);

        // InventoryLedgerQuery's own query shape: every movement for one (org, product,
        // warehouse), chronological.
        builder.HasIndex(x => new { x.OrganizationId, x.ProductId, x.WarehouseId, x.TransactionDate });
    }
}
