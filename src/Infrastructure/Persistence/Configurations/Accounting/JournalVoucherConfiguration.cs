using ErpApp.Domain.Accounting;
using ErpApp.Domain.Common;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Infrastructure.Persistence.Configurations.Accounting;

public sealed class JournalVoucherConfiguration : IEntityTypeConfiguration<JournalVoucher>
{
    public void Configure(EntityTypeBuilder<JournalVoucher> builder)
    {
        builder.ToTable("JournalVouchers", schema: "accounting");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.OrganizationId).IsRequired();
        builder.Property(x => x.Code).HasMaxLength(30).IsRequired();
        builder.Property(x => x.Date).IsRequired();
        builder.Property(x => x.Reference).HasMaxLength(200);
        // No HasDefaultValue -- Status is always set explicitly by JournalVoucher.Create.
        builder.Property(x => x.Status).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(x => x.CreatedAt).IsRequired();
        builder.Property(x => x.RowVersion).IsRowVersion();

        // Phase 28 (FR-2.5). Both carry a SQL default so the migration backfills every existing row
        // to "base currency at rate 1" without a data script, and ValueGeneratedNever so EF always
        // sends the aggregate's own value rather than ever falling back to that default (the
        // phase-2 bug #2 shape: a stored default silently winning over an in-memory value).
        builder.Property(x => x.CurrencyCode)
            .HasMaxLength(3).IsRequired().HasDefaultValue(CurrencyCatalog.BaseCode).ValueGeneratedNever();
        builder.Property(x => x.ExchangeRate)
            .HasPrecision(18, ExchangeRates.RateScale).IsRequired()
            .HasDefaultValue(ExchangeRates.BaseRate).ValueGeneratedNever();

        // Encapsulated child collection (private backing field), same mapping shape as
        // Catalog.Product.SecondaryUnits -- Cascade since JournalVoucherLine is owned by this
        // aggregate, not a reference to another one.
        builder.HasMany(x => x.Lines)
            .WithOne()
            .HasForeignKey("JournalVoucherId")
            .OnDelete(DeleteBehavior.Cascade);

        builder.Metadata.FindNavigation(nameof(JournalVoucher.Lines))!
            .SetPropertyAccessMode(PropertyAccessMode.Field);
    }
}
