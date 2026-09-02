using ErpApp.Domain.Catalog;
using ErpApp.Domain.Configuration;
using ErpApp.Domain.Manufacturing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ErpApp.Infrastructure.Persistence.Configurations.Manufacturing;

public sealed class ProductionOrderConfiguration : IEntityTypeConfiguration<ProductionOrder>
{
    public void Configure(EntityTypeBuilder<ProductionOrder> builder)
    {
        builder.ToTable("ProductionOrders", schema: "manufacturing");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.OrganizationId).IsRequired();
        builder.Property(x => x.Code).HasMaxLength(30).IsRequired();
        builder.Property(x => x.Date).IsRequired();
        builder.Property(x => x.Reference).HasMaxLength(200);
        builder.Property(x => x.OutputQuantity).HasPrecision(18, 4).IsRequired();
        builder.Property(x => x.Notes).HasMaxLength(2000);
        builder.Property(x => x.Status).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(x => x.CreatedAt).IsRequired();
        builder.Property(x => x.RowVersion).IsRowVersion();

        builder.HasOne<Product>().WithMany().HasForeignKey(x => x.ProductId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<BillOfMaterials>().WithMany().HasForeignKey(x => x.BillOfMaterialsId).OnDelete(DeleteBehavior.Restrict);

        builder.HasMany(x => x.RawMaterials).WithOne().HasForeignKey(x => x.ProductionOrderId).OnDelete(DeleteBehavior.Cascade);
        builder.HasMany(x => x.ByProducts).WithOne().HasForeignKey(x => x.ProductionOrderId).OnDelete(DeleteBehavior.Cascade);
        builder.HasMany(x => x.Expenses).WithOne().HasForeignKey(x => x.ProductionOrderId).OnDelete(DeleteBehavior.Cascade);

        builder.Metadata.FindNavigation(nameof(ProductionOrder.RawMaterials))!.SetPropertyAccessMode(PropertyAccessMode.Field);
        builder.Metadata.FindNavigation(nameof(ProductionOrder.ByProducts))!.SetPropertyAccessMode(PropertyAccessMode.Field);
        builder.Metadata.FindNavigation(nameof(ProductionOrder.Expenses))!.SetPropertyAccessMode(PropertyAccessMode.Field);
    }
}

public sealed class ProductionOrderRawMaterialLineConfiguration : IEntityTypeConfiguration<ProductionOrderRawMaterialLine>
{
    public void Configure(EntityTypeBuilder<ProductionOrderRawMaterialLine> builder)
    {
        builder.ToTable("ProductionOrderRawMaterialLines", schema: "manufacturing");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Quantity).HasPrecision(18, 4).IsRequired();
        builder.HasOne<Product>().WithMany().HasForeignKey(x => x.ProductId).OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class ProductionOrderByProductLineConfiguration : IEntityTypeConfiguration<ProductionOrderByProductLine>
{
    public void Configure(EntityTypeBuilder<ProductionOrderByProductLine> builder)
    {
        builder.ToTable("ProductionOrderByProductLines", schema: "manufacturing");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.CostAllocationPct).HasPrecision(9, 4).IsRequired();
        builder.Property(x => x.Quantity).HasPrecision(18, 4).IsRequired();
        builder.HasOne<Product>().WithMany().HasForeignKey(x => x.ProductId).OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class ProductionOrderExpenseLineConfiguration : IEntityTypeConfiguration<ProductionOrderExpenseLine>
{
    public void Configure(EntityTypeBuilder<ProductionOrderExpenseLine> builder)
    {
        builder.ToTable("ProductionOrderExpenseLines", schema: "manufacturing");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Amount).HasPrecision(18, 4).IsRequired();
        builder.HasOne<CostTerm>().WithMany().HasForeignKey(x => x.CostTermId).OnDelete(DeleteBehavior.Restrict);
    }
}
