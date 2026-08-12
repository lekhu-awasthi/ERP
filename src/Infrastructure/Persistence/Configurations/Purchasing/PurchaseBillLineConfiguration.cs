using ErpApp.Domain.Catalog;
using ErpApp.Domain.Purchasing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ErpApp.Infrastructure.Persistence.Configurations.Purchasing;

public sealed class PurchaseBillLineConfiguration : IEntityTypeConfiguration<PurchaseBillLine>
{
    public void Configure(EntityTypeBuilder<PurchaseBillLine> builder)
    {
        builder.ToTable("PurchaseBillLines", schema: "purchasing");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Quantity).HasPrecision(18, 4).IsRequired();
        builder.Property(x => x.Rate).HasPrecision(18, 4).IsRequired();
        builder.Property(x => x.VatRate).HasConversion<string>().HasMaxLength(30).IsRequired();
        builder.Property(x => x.Amount).HasPrecision(18, 4).IsRequired();
        builder.Property(x => x.VatAmount).HasPrecision(18, 4).IsRequired();
        builder.Property(x => x.ExpenditureClassification).HasConversion<string>().HasMaxLength(20).IsRequired();

        builder.HasOne<Product>().WithMany().HasForeignKey(x => x.ProductId).OnDelete(DeleteBehavior.Restrict);
    }
}
