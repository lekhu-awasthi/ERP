using ErpApp.Domain.Common;
using ErpApp.Domain.Configuration;
using ErpApp.Domain.Contacts;
using ErpApp.Domain.Purchasing;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Infrastructure.Persistence.Configurations.Purchasing;

public sealed class ExpenseConfiguration : IEntityTypeConfiguration<Expense>
{
    public void Configure(EntityTypeBuilder<Expense> builder)
    {
        builder.ToTable("Expenses", schema: "purchasing");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.OrganizationId).IsRequired();
        builder.Property(x => x.Code).HasMaxLength(30).IsRequired();
        builder.Property(x => x.Date).IsRequired();
        builder.Property(x => x.SupplierInvoiceReference).HasMaxLength(100);
        builder.Property(x => x.Notes).HasMaxLength(1000);
        builder.Property(x => x.TdsApplicable).IsRequired();
        builder.Property(x => x.TdsAmount).HasPrecision(18, 4).IsRequired();
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

        builder.Ignore(x => x.GrandTotal);

        builder.HasOne<Contact>().WithMany().HasForeignKey(x => x.ContactId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<TdsType>().WithMany().HasForeignKey(x => x.TdsTypeId).OnDelete(DeleteBehavior.Restrict);

        builder.HasMany(x => x.Lines)
            .WithOne()
            .HasForeignKey("ExpenseId")
            .OnDelete(DeleteBehavior.Cascade);

        builder.Metadata.FindNavigation(nameof(Expense.Lines))!
            .SetPropertyAccessMode(PropertyAccessMode.Field);
    }
}
