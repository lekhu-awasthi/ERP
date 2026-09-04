using ErpApp.Domain.Common;
using ErpApp.Domain.Configuration;
using ErpApp.Domain.Contacts;
using ErpApp.Domain.Sales;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Infrastructure.Persistence.Configurations.Sales;

public sealed class QuotationConfiguration : IEntityTypeConfiguration<Quotation>
{
    public void Configure(EntityTypeBuilder<Quotation> builder)
    {
        builder.ToTable("Quotations", schema: "sales");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.OrganizationId).IsRequired();
        builder.Property(x => x.Code).HasMaxLength(30).IsRequired();
        builder.Property(x => x.Date).IsRequired();
        builder.Property(x => x.Reference).HasMaxLength(200);
        builder.Property(x => x.Status).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(x => x.CreatedAt).IsRequired();
        builder.Property(x => x.RowVersion).IsRowVersion();
        // Phase 27b -- no HasMaxLength: the reference product's terms editor is a rich-text
        // box with no visible cap, and a truncated legal clause is a worse failure than a
        // wide column. nvarchar(max), same call this codebase already makes for Notes.
        builder.Property(x => x.Terms);
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

        builder.HasOne<Contact>().WithMany().HasForeignKey(x => x.ContactId).OnDelete(DeleteBehavior.Restrict);

        // SetNull, not Restrict/Cascade: CustomStatusId carries no GL/financial weight (Phase 20b),
        // so deleting the CustomStatus definition should just clear the assignment, not block the
        // delete or (Cascade's real risk here) delete the Quotation itself.
        builder.HasOne<CustomStatus>().WithMany().HasForeignKey(x => x.CustomStatusId).OnDelete(DeleteBehavior.SetNull);

        builder.HasMany(x => x.Lines)
            .WithOne()
            .HasForeignKey("QuotationId")
            .OnDelete(DeleteBehavior.Cascade);

        builder.Metadata.FindNavigation(nameof(Quotation.Lines))!
            .SetPropertyAccessMode(PropertyAccessMode.Field);
    }
}
