using ErpApp.Domain.Catalog;
using ErpApp.Domain.Purchasing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ErpApp.Infrastructure.Persistence.Configurations.Purchasing;

public sealed class DebitNoteLineConfiguration : IEntityTypeConfiguration<DebitNoteLine>
{
    public void Configure(EntityTypeBuilder<DebitNoteLine> builder)
    {
        builder.ToTable("DebitNoteLines", schema: "purchasing");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Quantity).HasPrecision(18, 4).IsRequired();
        builder.Property(x => x.Rate).HasPrecision(18, 4).IsRequired();
        builder.Property(x => x.VatRate).HasConversion<string>().HasMaxLength(30).IsRequired();
        builder.Property(x => x.DiscountPct).HasPrecision(18, 4).IsRequired();
        builder.Property(x => x.Amount).HasPrecision(18, 4).IsRequired();
        builder.Property(x => x.VatAmount).HasPrecision(18, 4).IsRequired();
        builder.Property(x => x.ConsumedUnitCost).HasPrecision(18, 4);

        builder.HasOne<Product>().WithMany().HasForeignKey(x => x.ProductId).OnDelete(DeleteBehavior.Restrict);

        // Phase 51 -- the batch this line receives into or issues from. Restrict, not Cascade:
        // deleting a batch that a document line names would silently rewrite an approved document.
        builder.HasOne<ProductBatch>().WithMany().HasForeignKey(x => x.BatchId).OnDelete(DeleteBehavior.Restrict);

    }
}
