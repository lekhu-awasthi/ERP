using ErpApp.Domain.Catalog;
using ErpApp.Domain.Inventory;
using ErpApp.Domain.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ErpApp.Infrastructure.Persistence.Configurations.Inventory;

public sealed class OpeningStockLineConfiguration : IEntityTypeConfiguration<OpeningStockLine>
{
    public void Configure(EntityTypeBuilder<OpeningStockLine> builder)
    {
        // Phase 32 -- the billing location this document was raised from. Restrict, mirroring the
        // Warehouse FK this codebase already uses: a location a document points at must not vanish
        // under it. Nullable, so a tenant whose LocationScopeMode excludes this type stores null.
        builder.HasOne<BillingLocation>().WithMany().HasForeignKey(x => x.LocationId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.ToTable("OpeningStockLines", schema: "inventory");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.OrganizationId).IsRequired();
        builder.Property(x => x.Quantity).HasColumnType("decimal(18,4)").IsRequired();
        builder.Property(x => x.Rate).HasColumnType("decimal(18,4)").IsRequired();
        builder.Property(x => x.CreatedAt).IsRequired();
        builder.Property(x => x.UpdatedAt).IsRequired();

        builder.HasIndex(x => new { x.OrganizationId, x.ProductId, x.WarehouseId }).IsUnique();

        builder.HasOne<Product>().WithMany().HasForeignKey(x => x.ProductId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Warehouse>().WithMany().HasForeignKey(x => x.WarehouseId).OnDelete(DeleteBehavior.Restrict);
    }
}
