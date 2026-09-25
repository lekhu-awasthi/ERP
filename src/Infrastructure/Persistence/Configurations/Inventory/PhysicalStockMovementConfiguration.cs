using ErpApp.Domain.Catalog;
using ErpApp.Domain.Inventory;
using ErpApp.Domain.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ErpApp.Infrastructure.Persistence.Configurations.Inventory;

/// <summary>
/// Phase 58 -- <see cref="StockMovementConfiguration"/>'s shape, minus cost, value, batch and serial.
///
/// <para>A table of its own rather than a discriminator column on <c>StockMovements</c>: nineteen
/// files read that table directly, every one of them means the accounting ledger, and a column would
/// have made each of them one forgotten <c>Where</c> away from counting a delivery note as a sale
/// (phase 35b's nine-handlers lesson). Here none of them can see a physical row, because none is
/// there.</para>
/// </summary>
public sealed class PhysicalStockMovementConfiguration : IEntityTypeConfiguration<PhysicalStockMovement>
{
    public void Configure(EntityTypeBuilder<PhysicalStockMovement> builder)
    {
        builder.ToTable("PhysicalStockMovements", schema: "inventory");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.OrganizationId).IsRequired();
        builder.Property(x => x.Direction).HasConversion<string>().HasMaxLength(10).IsRequired();
        builder.Property(x => x.Quantity).HasPrecision(18, 4).IsRequired();
        builder.Property(x => x.SourceDocumentType).HasConversion<string>().HasMaxLength(30).IsRequired();
        builder.Property(x => x.SourceDocumentId).IsRequired();
        builder.Property(x => x.TransactionDate).IsRequired();
        builder.Property(x => x.CreatedAt).IsRequired();
        builder.Property(x => x.LocationId);

        builder.HasOne<Product>().WithMany().HasForeignKey(x => x.ProductId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Warehouse>().WithMany().HasForeignKey(x => x.WarehouseId).OnDelete(DeleteBehavior.Restrict);

        // Measured on tools/scale's 200,000-row physical fixture (seed-phase58.sql, run-probe-
        // phase58.py), logical reads, all three paths in every variant (phase 34c's rule):
        //
        //   * the availability check (a Delivery Note's Approve, a GRN's Void) sums one product in
        //     one warehouse. Without INCLUDE the optimiser never chose this composite at all -- the
        //     ProductId FK index plus key lookups won, 318 reads either way. Covering Direction and
        //     Quantity takes it to 5.
        //   * a void reads its own rows back by source: 9 reads with the second index, a full scan
        //     (6,651) without.
        //   * the report load (Position in Physical mode, the Variance report's Actual) reads every
        //     row up to a date: the same full scan in every variant. No index helps a full read, so
        //     none is added for it -- measured and refused.
        builder.HasIndex(x => new { x.OrganizationId, x.ProductId, x.WarehouseId, x.TransactionDate })
            .IncludeProperties(x => new { x.Direction, x.Quantity });
        builder.HasIndex(x => new { x.OrganizationId, x.SourceDocumentType, x.SourceDocumentId });
    }
}
