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

        // Phase 51 -- the batch and serial dimensions. Both nullable and both un-indexed on their
        // own, for the same reason LocationId is: every reader narrows by (org, product, warehouse)
        // first through the composite below, and the dimension is a residual predicate over what
        // that has already narrowed. Phase 34c measured what a second index over the same table does
        // to the paths that do not use it, and phase 50 measured it again.
        builder.Property(x => x.BatchId);
        builder.Property(x => x.SerialNo).HasMaxLength(Domain.Inventory.DocumentLineSerial.SerialNoMaxLength);

        builder.HasOne<ProductBatch>().WithMany().HasForeignKey(x => x.BatchId).OnDelete(DeleteBehavior.Restrict);

        // Phase 51 -- at most one *in-stock* layer per serial number, per product, per tenant.
        //
        // The filter is two conditions and both are load-bearing. `SerialNo IS NOT NULL` is
        // CLAUDE.md's standing rule: SQL Server treats NULLs as equal in a unique index, so without
        // it every non-serialised layer in the tenant would collide with every other.
        // `QuantityRemaining > 0` is the one that encodes the model: a serial that is issued and
        // later returned re-enters stock as a NEW layer with the same number, while the old row
        // stays as the history the kardex reconstructs from -- so uniqueness has to be over what is
        // in stock, not over all of it.
        //
        // InMemory enforces neither half, so the race this protects against is verified against real
        // SQL Server (phase 20e Decision C).
        builder.HasIndex(x => new { x.OrganizationId, x.ProductId, x.SerialNo })
            .IsUnique()
            .HasFilter("[SerialNo] IS NOT NULL AND [QuantityRemaining] > 0");

        builder.HasOne<Product>().WithMany().HasForeignKey(x => x.ProductId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Warehouse>().WithMany().HasForeignKey(x => x.WarehouseId).OnDelete(DeleteBehavior.Restrict);

        // The FIFO engine's own query shape: every layer for one (org, product, warehouse) with
        // remaining quantity, ordered oldest-TransactionDate-first (IStockLedgerService).
        builder.HasIndex(x => new { x.OrganizationId, x.ProductId, x.WarehouseId, x.TransactionDate });
    }
}
