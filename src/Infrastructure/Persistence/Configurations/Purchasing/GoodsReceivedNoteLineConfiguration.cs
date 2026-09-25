using ErpApp.Domain.Catalog;
using ErpApp.Domain.Common;
using ErpApp.Domain.Purchasing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ErpApp.Infrastructure.Persistence.Configurations.Purchasing;

/// <summary>Phase 58 -- <see cref="PurchaseOrderLineConfiguration"/>'s shape, including phase 52's
/// unit and frozen factor.</summary>
public sealed class GoodsReceivedNoteLineConfiguration : IEntityTypeConfiguration<GoodsReceivedNoteLine>
{
    public void Configure(EntityTypeBuilder<GoodsReceivedNoteLine> builder)
    {
        builder.ToTable("GoodsReceivedNoteLines", schema: "purchasing");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Quantity).HasPrecision(18, 4).IsRequired();
        builder.Property(x => x.Rate).HasPrecision(18, 4).IsRequired();
        builder.Property(x => x.VatRate).HasConversion<string>().HasMaxLength(30).IsRequired();
        builder.Property(x => x.DiscountPct).HasPrecision(18, 4).IsRequired();
        builder.Property(x => x.Amount).HasPrecision(18, 4).IsRequired();
        builder.Property(x => x.VatAmount).HasPrecision(18, 4).IsRequired();
        builder.Property(x => x.ConversionFactor)
            .HasPrecision(18, UnitConversion.FactorScale)
            .HasDefaultValue(UnitConversion.PrimaryFactor)
            .IsRequired();

        builder.HasOne<Product>().WithMany().HasForeignKey(x => x.ProductId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<UnitOfMeasurement>().WithMany().HasForeignKey(x => x.UnitId).OnDelete(DeleteBehavior.Restrict);

        builder.Ignore(x => x.PrimaryQuantity);
    }
}
