using ErpApp.Domain.Common;
using ErpApp.Domain.Configuration;
using ErpApp.Domain.Contacts;
using ErpApp.Domain.Purchasing;
using ErpApp.Domain.Tenancy;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Infrastructure.Persistence.Configurations.Purchasing;

public sealed class PurchaseBillConfiguration : IEntityTypeConfiguration<PurchaseBill>
{
    public void Configure(EntityTypeBuilder<PurchaseBill> builder)
    {
        builder.ToTable("PurchaseBills", schema: "purchasing");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.OrganizationId).IsRequired();
        builder.Property(x => x.Code).HasMaxLength(30).IsRequired();
        builder.Property(x => x.Date).IsRequired();
        builder.Property(x => x.Reference).HasMaxLength(200);
        builder.Property(x => x.SupplierInvoiceReference).HasMaxLength(100);
        builder.Property(x => x.IsImport).IsRequired();
        builder.Property(x => x.ImportCountry).HasMaxLength(100);
        builder.Property(x => x.ImportDocumentNo).HasMaxLength(100);
        builder.Property(x => x.TdsAmount).HasPrecision(18, 4).IsRequired();
        builder.Property(x => x.Status).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(x => x.CreatedAt).IsRequired();
        builder.Property(x => x.RowVersion).IsRowVersion();
        // architecture-spec.md §3.3's document-conversion columns -- null for a standalone
        // PurchaseBill, set when created via GetPurchaseBillConversionTemplate's pre-filled
        // CreatePurchaseBillCommand.
        builder.Property(x => x.ReferrerType).HasConversion<string>().HasMaxLength(30);
        builder.Property(x => x.ReferrerId);
        builder.Property(x => x.DiscountPct).HasPrecision(18, 4).IsRequired();

        // Phase 28 (FR-2.5). Both carry a SQL default so the migration backfills every existing row
        // to "base currency at rate 1" without a data script, and ValueGeneratedNever so EF always
        // sends the aggregate's own value rather than ever falling back to that default (the
        // phase-2 bug #2 shape: a stored default silently winning over an in-memory value).
        builder.Property(x => x.CurrencyCode)
            .HasMaxLength(3).IsRequired().HasDefaultValue(CurrencyCatalog.BaseCode).ValueGeneratedNever();
        builder.Property(x => x.ExchangeRate)
            .HasPrecision(18, ExchangeRates.RateScale).IsRequired()
            .HasDefaultValue(ExchangeRates.BaseRate).ValueGeneratedNever();

        // Phase 29 (FR-6.15). The two capitalisation figures are nullable and in base currency,
        // written once at Approve; the flag carries a SQL default so the migration backfills every
        // existing row without a data script, with ValueGeneratedNever for the phase-2 bug #2 reason.
        builder.Property(x => x.IsProductWiseAdditionalCost)
            .IsRequired().HasDefaultValue(false).ValueGeneratedNever();
        builder.Property(x => x.CapitalisedAdditionalCost).HasPrecision(18, PurchaseBill.AllocationScale);
        builder.Property(x => x.AdditionalCostRoundingAdjustment).HasPrecision(18, PurchaseBill.AllocationScale);

        builder.Ignore(x => x.GrandTotal);
        builder.Ignore(x => x.AdditionalCostTotal);

        builder.HasOne<Contact>().WithMany().HasForeignKey(x => x.ContactId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Warehouse>().WithMany().HasForeignKey(x => x.WarehouseId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<TdsType>().WithMany().HasForeignKey(x => x.TdsTypeId).OnDelete(DeleteBehavior.Restrict);

        builder.HasMany(x => x.Lines)
            .WithOne()
            .HasForeignKey("PurchaseBillId")
            .OnDelete(DeleteBehavior.Cascade);

        builder.Metadata.FindNavigation(nameof(PurchaseBill.Lines))!
            .SetPropertyAccessMode(PropertyAccessMode.Field);

        builder.HasMany(x => x.AdditionalCosts)
            .WithOne()
            .HasForeignKey(x => x.PurchaseBillId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Metadata.FindNavigation(nameof(PurchaseBill.AdditionalCosts))!
            .SetPropertyAccessMode(PropertyAccessMode.Field);
    }
}
