using ErpApp.Domain.Common;
using ErpApp.Domain.Contacts;
using ErpApp.Domain.Sales;
using ErpApp.Domain.Tenancy;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Infrastructure.Persistence.Configurations.Sales;

public sealed class InvoiceConfiguration : IEntityTypeConfiguration<Invoice>
{
    public void Configure(EntityTypeBuilder<Invoice> builder)
    {
        builder.ToTable("Invoices", schema: "sales");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.OrganizationId).IsRequired();
        builder.Property(x => x.Code).HasMaxLength(30).IsRequired();
        builder.Property(x => x.Date).IsRequired();
        builder.Property(x => x.Reference).HasMaxLength(200);
        // FR-5.8's export-sale block, mirroring PurchaseBillConfiguration's IsImport/ImportCountry/
        // ImportDocumentNo lengths so the two read the same. Nullable detail fields: unlike the
        // import block they stay optional even when the flag is set (live-confirmed).
        builder.Property(x => x.IsExport).IsRequired();
        builder.Property(x => x.ExportCountry).HasMaxLength(100);
        builder.Property(x => x.ExportDeclarationNo).HasMaxLength(100);
        builder.Property(x => x.Status).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(x => x.CreatedAt).IsRequired();
        builder.Property(x => x.RowVersion).IsRowVersion();
        // Phase 27b -- no HasMaxLength: the reference product's terms editor is a rich-text
        // box with no visible cap, and a truncated legal clause is a worse failure than a
        // wide column. nvarchar(max), same call this codebase already makes for Notes.
        builder.Property(x => x.Terms);
        // architecture-spec.md §3.3's document-conversion columns -- null for a standalone Invoice,
        // set when created via GetInvoiceConversionTemplate's pre-filled CreateInvoiceCommand.
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

        builder.Ignore(x => x.GrandTotal);

        builder.HasOne<Contact>().WithMany().HasForeignKey(x => x.ContactId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Warehouse>().WithMany().HasForeignKey(x => x.WarehouseId).OnDelete(DeleteBehavior.Restrict);

        builder.HasMany(x => x.Lines)
            .WithOne()
            .HasForeignKey("InvoiceId")
            .OnDelete(DeleteBehavior.Cascade);

        builder.Metadata.FindNavigation(nameof(Invoice.Lines))!
            .SetPropertyAccessMode(PropertyAccessMode.Field);
    }
}
