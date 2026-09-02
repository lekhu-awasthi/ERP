using ErpApp.Domain.Catalog;
using ErpApp.Domain.Configuration;
using ErpApp.Domain.Manufacturing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ErpApp.Infrastructure.Persistence.Configurations.Manufacturing;

public sealed class BillOfMaterialsConfiguration : IEntityTypeConfiguration<BillOfMaterials>
{
    public void Configure(EntityTypeBuilder<BillOfMaterials> builder)
    {
        builder.ToTable("BillsOfMaterials", schema: "manufacturing");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.OrganizationId).IsRequired();
        builder.Property(x => x.OutputQuantity).HasPrecision(18, 4).IsRequired();
        builder.Property(x => x.ManufactureOnEverySale).IsRequired();
        builder.Property(x => x.Notes).HasMaxLength(2000);
        builder.Property(x => x.IsActive).IsRequired();
        builder.Property(x => x.CreatedAt).IsRequired();

        builder.HasOne<Product>().WithMany().HasForeignKey(x => x.ProductId).OnDelete(DeleteBehavior.Restrict);

        // At most one BOM per finished product -- live-confirmed by the absence of a BOM picker on
        // the Production Order/Journal forms' LOAD BOM action: it resolves the recipe from the
        // chosen product alone, so a second BOM for the same product could never be reached.
        // Not a filtered index: ProductId is non-nullable here, so phase-24's nullable-column
        // caveat does not apply.
        builder.HasIndex(x => new { x.OrganizationId, x.ProductId }).IsUnique();

        builder.HasMany(x => x.RawMaterials).WithOne().HasForeignKey(x => x.BillOfMaterialsId).OnDelete(DeleteBehavior.Cascade);
        builder.HasMany(x => x.ByProducts).WithOne().HasForeignKey(x => x.BillOfMaterialsId).OnDelete(DeleteBehavior.Cascade);
        builder.HasMany(x => x.Expenses).WithOne().HasForeignKey(x => x.BillOfMaterialsId).OnDelete(DeleteBehavior.Cascade);

        builder.Metadata.FindNavigation(nameof(BillOfMaterials.RawMaterials))!.SetPropertyAccessMode(PropertyAccessMode.Field);
        builder.Metadata.FindNavigation(nameof(BillOfMaterials.ByProducts))!.SetPropertyAccessMode(PropertyAccessMode.Field);
        builder.Metadata.FindNavigation(nameof(BillOfMaterials.Expenses))!.SetPropertyAccessMode(PropertyAccessMode.Field);
    }
}

public sealed class BomRawMaterialLineConfiguration : IEntityTypeConfiguration<BomRawMaterialLine>
{
    public void Configure(EntityTypeBuilder<BomRawMaterialLine> builder)
    {
        builder.ToTable("BomRawMaterialLines", schema: "manufacturing");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Quantity).HasPrecision(18, 4).IsRequired();
        builder.HasOne<Product>().WithMany().HasForeignKey(x => x.ProductId).OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class BomByProductLineConfiguration : IEntityTypeConfiguration<BomByProductLine>
{
    public void Configure(EntityTypeBuilder<BomByProductLine> builder)
    {
        builder.ToTable("BomByProductLines", schema: "manufacturing");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.CostAllocationPct).HasPrecision(9, 4).IsRequired();
        builder.Property(x => x.Quantity).HasPrecision(18, 4).IsRequired();
        builder.HasOne<Product>().WithMany().HasForeignKey(x => x.ProductId).OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class BomExpenseLineConfiguration : IEntityTypeConfiguration<BomExpenseLine>
{
    public void Configure(EntityTypeBuilder<BomExpenseLine> builder)
    {
        builder.ToTable("BomExpenseLines", schema: "manufacturing");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Amount).HasPrecision(18, 4).IsRequired();
        builder.HasOne<CostTerm>().WithMany().HasForeignKey(x => x.CostTermId).OnDelete(DeleteBehavior.Restrict);
    }
}
