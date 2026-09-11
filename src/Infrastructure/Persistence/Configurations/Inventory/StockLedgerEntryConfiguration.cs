using ErpApp.Domain.Catalog;
using ErpApp.Domain.Inventory;
using ErpApp.Domain.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ErpApp.Infrastructure.Persistence.Configurations.Inventory;

public sealed class StockLedgerEntryConfiguration : IEntityTypeConfiguration<StockLedgerEntry>
{
    public void Configure(EntityTypeBuilder<StockLedgerEntry> builder)
    {
        builder.ToTable("StockLedgerEntries", schema: "inventory");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.OrganizationId).IsRequired();
        builder.Property(x => x.SourceDocumentType).HasConversion<string>().HasMaxLength(30).IsRequired();
        builder.Property(x => x.SourceDocumentId).IsRequired();
        builder.Property(x => x.QuantityIn).HasPrecision(18, 4).IsRequired();
        builder.Property(x => x.QuantityRemaining).HasPrecision(18, 4).IsRequired();
        builder.Property(x => x.UnitCost).HasPrecision(18, 4).IsRequired();
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

        // The FIFO engine's own query shape: every layer for one (org, product, warehouse) with
        // remaining quantity, ordered oldest-TransactionDate-first (IStockLedgerService).
        builder.HasIndex(x => new { x.OrganizationId, x.ProductId, x.WarehouseId, x.TransactionDate });
    }
}
