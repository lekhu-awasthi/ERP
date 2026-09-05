using ErpApp.Domain.Catalog;
using ErpApp.Domain.Configuration;
using ErpApp.Domain.Purchasing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ErpApp.Infrastructure.Persistence.Configurations.Purchasing;

public sealed class PurchaseBillAdditionalCostConfiguration : IEntityTypeConfiguration<PurchaseBillAdditionalCost>
{
    public void Configure(EntityTypeBuilder<PurchaseBillAdditionalCost> builder)
    {
        builder.ToTable("PurchaseBillAdditionalCosts", schema: "purchasing");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Method).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(x => x.Amount).HasPrecision(18, PurchaseBill.AllocationScale).IsRequired();

        builder.HasOne<CostTerm>().WithMany().HasForeignKey(x => x.CostTermId).OnDelete(DeleteBehavior.Restrict);

        // Nullable: null is the live picker's "All Product". Restrict, like every other product
        // reference on a transactional document.
        builder.HasOne<Product>().WithMany().HasForeignKey(x => x.ProductId).OnDelete(DeleteBehavior.Restrict);

        builder.HasMany(x => x.Allocations)
            .WithOne()
            .HasForeignKey(x => x.PurchaseBillAdditionalCostId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Metadata.FindNavigation(nameof(PurchaseBillAdditionalCost.Allocations))!
            .SetPropertyAccessMode(PropertyAccessMode.Field);
    }
}

public sealed class PurchaseBillAdditionalCostAllocationConfiguration
    : IEntityTypeConfiguration<PurchaseBillAdditionalCostAllocation>
{
    public void Configure(EntityTypeBuilder<PurchaseBillAdditionalCostAllocation> builder)
    {
        builder.ToTable("PurchaseBillAdditionalCostAllocations", schema: "purchasing");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Amount).HasPrecision(18, PurchaseBill.AllocationScale).IsRequired();

        builder.HasOne<PurchaseBillLine>()
            .WithMany()
            .HasForeignKey(x => x.PurchaseBillLineId)
            // Restrict, not Cascade: an allocation only ever exists on an Approved bill, whose lines
            // are never replaced again, and two cascade paths into the same row (via the cost row
            // and via the line) is a multiple-cascade-path error on SQL Server anyway.
            .OnDelete(DeleteBehavior.Restrict);
    }
}
