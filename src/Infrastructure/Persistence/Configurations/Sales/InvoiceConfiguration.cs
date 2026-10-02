using ErpApp.Domain.Common;
using ErpApp.Domain.Contacts;
using ErpApp.Domain.Pos;
using ErpApp.Domain.Sales;
using ErpApp.Domain.Tenancy;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Infrastructure.Persistence.Configurations.Sales;

public sealed class InvoiceConfiguration : IEntityTypeConfiguration<Invoice>
{
    public void Configure(EntityTypeBuilder<Invoice> builder)
    {
        // Phase 32 -- the billing location this document was raised from. Restrict, mirroring the
        // Warehouse FK this codebase already uses: a location a document points at must not vanish
        // under it. Nullable, so a tenant whose LocationScopeMode excludes this type stores null.
        builder.HasOne<BillingLocation>().WithMany().HasForeignKey(x => x.LocationId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.ToTable("Invoices", schema: "sales");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.OrganizationId).IsRequired();
        builder.Property(x => x.Code).HasMaxLength(30).IsRequired();
        builder.Property(x => x.Date).IsRequired();

        // Phase 31 -- required, never null. The migration adds it with a placeholder default and
        // then backfills every existing row to that row's own Date in the same migration, which is
        // the value the ageing reports were already improvising; the default is dropped afterwards
        // so a future insert cannot silently take it.
        builder.Property(x => x.DueDate).IsRequired();
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
        builder.Ignore(x => x.ServiceChargeTotal);
        builder.Ignore(x => x.TenderedAmount);
        builder.Ignore(x => x.SettledAmount);
        builder.Ignore(x => x.CreditAmount);

        // Phase 61 -- the till sale's header. Channel defaults to Erp, which is true of every row
        // that existed before this phase; ValueGeneratedNever so EF never substitutes the SQL default
        // for an in-memory Erp (phase 2's enum-default gotcha, harmless here only because the member
        // is 0 -- stated anyway so a reordering cannot make it harmful).
        builder.Property(x => x.Channel)
            .HasConversion<string>().HasMaxLength(10).IsRequired()
            .HasDefaultValue(SalesChannel.Erp).ValueGeneratedNever();
        builder.Property(x => x.OrderType).HasConversion<string>().HasMaxLength(20);
        builder.Property(x => x.RoundOff).HasPrecision(18, 4).IsRequired().HasDefaultValue(0m).ValueGeneratedNever();
        builder.Property(x => x.ChangeAmount).HasPrecision(18, 4).IsRequired().HasDefaultValue(0m).ValueGeneratedNever();

        builder.HasOne<PosSession>()
            .WithMany()
            .HasForeignKey(x => x.PosSessionId)
            .OnDelete(DeleteBehavior.Restrict);

        // Phase 65 -- the restaurant order a till sale bills. Restrict: an order with bills is history,
        // never deleted. The FK's own index is what finds an order's bills.
        builder.HasOne<PosOrder>()
            .WithMany()
            .HasForeignKey(x => x.PosOrderId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasMany(x => x.Tenders)
            .WithOne()
            .HasForeignKey(x => x.InvoiceId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Metadata.FindNavigation(nameof(Invoice.Tenders))!
            .SetPropertyAccessMode(PropertyAccessMode.Field);

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
