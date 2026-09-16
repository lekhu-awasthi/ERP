using ErpApp.Domain.Catalog;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ErpApp.Infrastructure.Persistence.Configurations.Catalog;

public sealed class ProductBatchConfiguration : IEntityTypeConfiguration<ProductBatch>
{
    public void Configure(EntityTypeBuilder<ProductBatch> builder)
    {
        builder.ToTable("ProductBatches", schema: "catalog");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.OrganizationId).IsRequired();
        builder.Property(x => x.ProductId).IsRequired();
        builder.Property(x => x.BatchNo).HasMaxLength(ProductBatch.BatchNoMaxLength).IsRequired();
        builder.Property(x => x.CreatedAt).IsRequired();

        // Phase 51 -- both dates nullable, on purpose. The 2026-09-16 read observed one row
        // carrying both, which is one sample, and a lot number with no expiry is ordinary. A
        // consequence worth naming rather than discovering: TenantIndexConvention inspects only
        // *required* DateOnly properties, so this entity classifies as master data -- and it does so
        // by rule, because the unique index below leads with OrganizationId exactly like every other
        // master-data table. Infrastructure.UnitTests asserts both halves, so a later phase making
        // either date required is told by a failing test that it now owes the convention a
        // declaration (phase 50's lesson about being right by luck rather than by rule).
        builder.Property(x => x.ManufactureDate);
        builder.Property(x => x.ExpiryDate);

        builder.HasOne<Product>().WithMany().HasForeignKey(x => x.ProductId).OnDelete(DeleteBehavior.Restrict);

        // A batch number identifies a batch *of one product*, per tenant. Unfiltered because no
        // column in the key is nullable, so SQL Server's treat-NULLs-as-equal rule never applies
        // here (CLAUDE.md's nullable-unique-index gotcha is about the other shape).
        builder.HasIndex(x => new { x.OrganizationId, x.ProductId, x.BatchNo }).IsUnique();
    }
}
