using ErpApp.Domain.Catalog;
using ErpApp.Domain.Configuration;
using ErpApp.Domain.Manufacturing;
using ErpApp.Domain.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ErpApp.Infrastructure.Persistence.Configurations.Manufacturing;

public sealed class ProductionJournalConfiguration : IEntityTypeConfiguration<ProductionJournal>
{
    public void Configure(EntityTypeBuilder<ProductionJournal> builder)
    {
        builder.ToTable("ProductionJournals", schema: "manufacturing");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.OrganizationId).IsRequired();
        builder.Property(x => x.Code).HasMaxLength(30).IsRequired();
        builder.Property(x => x.Date).IsRequired();
        builder.Property(x => x.Reference).HasMaxLength(200);
        builder.Property(x => x.OutputQuantity).HasPrecision(18, 4).IsRequired();
        builder.Property(x => x.Notes).HasMaxLength(2000);
        builder.Property(x => x.Status).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(x => x.ReferrerType).HasConversion<string>().HasMaxLength(40);
        builder.Property(x => x.CreatedAt).IsRequired();
        builder.Property(x => x.RowVersion).IsRowVersion();

        // The roll-up figures stamped at Approve. Money at (18,4) throughout, and the two unit
        // costs deliberately at StockLedgerEntry.UnitCost's exact scale: FinishedGoodsUnitCost IS
        // the unit cost of the FIFO layer this document creates, so any other scale here would
        // guarantee the document and the ledger disagree. See ProductionJournal.UnitCostScale.
        builder.Property(x => x.RawMaterialCost).HasPrecision(18, 4);
        builder.Property(x => x.ProductionExpenseCost).HasPrecision(18, 4);
        builder.Property(x => x.CostAllocatedToByProduct).HasPrecision(18, 4);
        builder.Property(x => x.FinishedGoodsCost).HasPrecision(18, 4);
        builder.Property(x => x.FinishedGoodsUnitCost).HasPrecision(18, 4);

        // Derived from stored figures, exactly like Invoice.GrandTotal: nothing to persist.
        builder.Ignore(x => x.TotalCostOfProduction);
        builder.Ignore(x => x.CostRoundingAdjustment);

        builder.HasOne<Product>().WithMany().HasForeignKey(x => x.ProductId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Warehouse>().WithMany().HasForeignKey(x => x.WarehouseId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<BillOfMaterials>().WithMany().HasForeignKey(x => x.BillOfMaterialsId).OnDelete(DeleteBehavior.Restrict);

        builder.HasMany(x => x.RawMaterials).WithOne().HasForeignKey(x => x.ProductionJournalId).OnDelete(DeleteBehavior.Cascade);
        builder.HasMany(x => x.ByProducts).WithOne().HasForeignKey(x => x.ProductionJournalId).OnDelete(DeleteBehavior.Cascade);
        builder.HasMany(x => x.Expenses).WithOne().HasForeignKey(x => x.ProductionJournalId).OnDelete(DeleteBehavior.Cascade);

        builder.Metadata.FindNavigation(nameof(ProductionJournal.RawMaterials))!.SetPropertyAccessMode(PropertyAccessMode.Field);
        builder.Metadata.FindNavigation(nameof(ProductionJournal.ByProducts))!.SetPropertyAccessMode(PropertyAccessMode.Field);
        builder.Metadata.FindNavigation(nameof(ProductionJournal.Expenses))!.SetPropertyAccessMode(PropertyAccessMode.Field);
    }
}

public sealed class ProductionJournalRawMaterialLineConfiguration : IEntityTypeConfiguration<ProductionJournalRawMaterialLine>
{
    public void Configure(EntityTypeBuilder<ProductionJournalRawMaterialLine> builder)
    {
        builder.ToTable("ProductionJournalRawMaterialLines", schema: "manufacturing");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Quantity).HasPrecision(18, 4).IsRequired();
        builder.Property(x => x.ConsumedUnitCost).HasPrecision(18, 4);
        builder.Property(x => x.Amount).HasPrecision(18, 4);
        builder.HasOne<Product>().WithMany().HasForeignKey(x => x.ProductId).OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class ProductionJournalByProductLineConfiguration : IEntityTypeConfiguration<ProductionJournalByProductLine>
{
    public void Configure(EntityTypeBuilder<ProductionJournalByProductLine> builder)
    {
        builder.ToTable("ProductionJournalByProductLines", schema: "manufacturing");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.CostAllocationPct).HasPrecision(9, 4).IsRequired();
        builder.Property(x => x.Quantity).HasPrecision(18, 4).IsRequired();
        builder.Property(x => x.AllocatedUnitCost).HasPrecision(18, 4);
        builder.Property(x => x.AllocatedAmount).HasPrecision(18, 4);
        builder.HasOne<Product>().WithMany().HasForeignKey(x => x.ProductId).OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class ProductionJournalExpenseLineConfiguration : IEntityTypeConfiguration<ProductionJournalExpenseLine>
{
    public void Configure(EntityTypeBuilder<ProductionJournalExpenseLine> builder)
    {
        builder.ToTable("ProductionJournalExpenseLines", schema: "manufacturing");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Amount).HasPrecision(18, 4).IsRequired();
        builder.HasOne<CostTerm>().WithMany().HasForeignKey(x => x.CostTermId).OnDelete(DeleteBehavior.Restrict);
    }
}
