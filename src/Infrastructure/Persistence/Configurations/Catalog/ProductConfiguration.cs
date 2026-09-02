using ErpApp.Domain.Accounting;
using ErpApp.Domain.Catalog;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ErpApp.Infrastructure.Persistence.Configurations.Catalog;

public sealed class ProductConfiguration : IEntityTypeConfiguration<Product>
{
    public void Configure(EntityTypeBuilder<Product> builder)
    {
        builder.ToTable("Products", schema: "catalog");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.OrganizationId).IsRequired();
        // No HasDefaultValue on Type/VatRate/ValuationMethod -- all three are always set
        // explicitly by Product.Create, so the enum-default EF gotcha never applies.
        builder.Property(x => x.Type).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(x => x.Name).HasMaxLength(200).IsRequired();
        builder.Property(x => x.Code).HasMaxLength(30).IsRequired();
        builder.Property(x => x.HsCode).HasMaxLength(30);
        builder.Property(x => x.AvailableForSale).IsRequired();
        builder.Property(x => x.SellingPrice).HasPrecision(18, 4).IsRequired();
        builder.Property(x => x.PurchasePrice).HasPrecision(18, 4).IsRequired();
        builder.Property(x => x.VatRate).HasConversion<string>().HasMaxLength(30).IsRequired();
        builder.Property(x => x.ValuationMethod).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(x => x.ReOrderLevel).IsRequired();
        builder.Property(x => x.TrackInventory).IsRequired();
        builder.Property(x => x.IsActive).IsRequired();
        builder.Property(x => x.CreatedAt).IsRequired();

        // Phase 24 (FR-8.3). Sku/Barcode are plain nullable identifiers -- deliberately NOT unique
        // indexed: the reference tenant shows duplicate/blank SKUs freely, and a tenant migrating
        // real data must not be blocked by a uniqueness rule the source system never had.
        builder.Property(x => x.Sku).HasMaxLength(60);
        builder.Property(x => x.Barcode).HasMaxLength(60);
        builder.Property(x => x.HasVariants).IsRequired();
        builder.Property(x => x.CombinationKey).HasMaxLength(600);

        builder.HasIndex(x => new { x.OrganizationId, x.Code }).IsUnique();

        // The duplicate guard that makes matrix generation idempotent. Filtered so it applies only
        // to variant children -- without the filter every ordinary product (CombinationKey null)
        // would collide, because SQL Server's unique indexes treat NULLs as equal to each other.
        builder.HasIndex(x => new { x.OrganizationId, x.ParentProductId, x.CombinationKey })
            .IsUnique()
            .HasFilter("[ParentProductId] IS NOT NULL");

        // Self-reference: a variant child points at its parent. Restrict, so a parent carrying
        // variants cannot be deleted out from under them.
        builder.HasOne<Product>()
            .WithMany()
            .HasForeignKey(x => x.ParentProductId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<ProductCategory>()
            .WithMany()
            .HasForeignKey(x => x.CategoryId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<UnitOfMeasurement>()
            .WithMany()
            .HasForeignKey(x => x.PrimaryUnitId)
            .OnDelete(DeleteBehavior.Restrict);

        // Phase 4 backfill (deferred from Phase 3 until Accounting.Account existed) -- see
        // Product's doc comment. Restrict, same FK delete behavior used everywhere else in this
        // codebase for a reference to another aggregate.
        builder.HasOne<Account>().WithMany().HasForeignKey(x => x.SalesAccountId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Account>().WithMany().HasForeignKey(x => x.SalesReturnAccountId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Account>().WithMany().HasForeignKey(x => x.PurchaseAccountId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Account>().WithMany().HasForeignKey(x => x.PurchaseReturnAccountId).OnDelete(DeleteBehavior.Restrict);

        // Encapsulated child collection (private backing field) -- first of its kind in this
        // codebase. Cascade here specifically: deleting a Product should delete its own
        // secondary-unit rows, unlike the Restrict-everywhere-else rule used for references to
        // other aggregates (ProductCategory/UnitOfMeasurement above).
        builder.HasMany(x => x.SecondaryUnits)
            .WithOne()
            .HasForeignKey("ProductId")
            .OnDelete(DeleteBehavior.Cascade);

        builder.Metadata.FindNavigation(nameof(Product.SecondaryUnits))!
            .SetPropertyAccessMode(PropertyAccessMode.Field);

        // Phase 24's two encapsulated child collections, same shape as SecondaryUnits above.
        // A parent's offered option pool ...
        builder.HasMany(x => x.VariantAttributeUsages)
            .WithOne()
            .HasForeignKey(x => x.ProductId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Metadata.FindNavigation(nameof(Product.VariantAttributeUsages))!
            .SetPropertyAccessMode(PropertyAccessMode.Field);

        // ... and a child's own combination.
        builder.HasMany(x => x.VariantValues)
            .WithOne()
            .HasForeignKey(x => x.ProductId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Metadata.FindNavigation(nameof(Product.VariantValues))!
            .SetPropertyAccessMode(PropertyAccessMode.Field);
    }
}
