using ErpApp.Domain.Accounting;
using ErpApp.Domain.Common;
using ErpApp.Domain.Configuration;
using ErpApp.Domain.Contacts;
using ErpApp.Domain.Payments;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Infrastructure.Persistence.Configurations.Payments;

public sealed class PaymentConfiguration : IEntityTypeConfiguration<Payment>
{
    public void Configure(EntityTypeBuilder<Payment> builder)
    {
        builder.ToTable("Payments", schema: "payments");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.OrganizationId).IsRequired();
        builder.Property(x => x.Direction).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(x => x.Code).HasMaxLength(30).IsRequired();
        builder.Property(x => x.Date).IsRequired();
        builder.Property(x => x.Amount).HasPrecision(18, 4).IsRequired();
        builder.Property(x => x.Reference).HasMaxLength(200);
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

        builder.HasOne<Contact>().WithMany().HasForeignKey(x => x.ContactId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<PaymentMode>().WithMany().HasForeignKey(x => x.PaymentModeId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Account>().WithMany().HasForeignKey(x => x.AccountId).OnDelete(DeleteBehavior.Restrict);

        // No EF navigation for Allocations anymore -- PaymentAllocation.SourceId is polymorphic
        // (docs/phase-17-status.md decision #2), so it can't be a real FK-constrained child
        // collection scoped to just this table. Handlers query PaymentAllocations directly
        // (SourceType=Payment, SourceId=this.Id) and call Payment.AttachAllocations to hydrate.
        builder.Ignore(x => x.Allocations);
    }
}
