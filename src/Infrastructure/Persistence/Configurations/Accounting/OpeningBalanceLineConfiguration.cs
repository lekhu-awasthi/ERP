using ErpApp.Domain.Accounting;
using ErpApp.Domain.Common;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Infrastructure.Persistence.Configurations.Accounting;

public sealed class OpeningBalanceLineConfiguration : IEntityTypeConfiguration<OpeningBalanceLine>
{
    public void Configure(EntityTypeBuilder<OpeningBalanceLine> builder)
    {
        builder.ToTable("OpeningBalanceLines", schema: "accounting");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.OrganizationId).IsRequired();
        builder.Property(x => x.Debit).HasColumnType("decimal(18,2)").IsRequired();
        builder.Property(x => x.Credit).HasColumnType("decimal(18,2)").IsRequired();
        builder.Property(x => x.CreatedAt).IsRequired();
        builder.Property(x => x.UpdatedAt).IsRequired();

        // Phase 28 (FR-2.5). Both carry a SQL default so the migration backfills every existing row
        // to "base currency at rate 1" without a data script, and ValueGeneratedNever so EF always
        // sends the aggregate's own value rather than ever falling back to that default (the
        // phase-2 bug #2 shape: a stored default silently winning over an in-memory value).
        builder.Property(x => x.CurrencyCode)
            .HasMaxLength(3).IsRequired().HasDefaultValue(CurrencyCatalog.BaseCode).ValueGeneratedNever();
        builder.Property(x => x.ExchangeRate)
            .HasPrecision(18, ExchangeRates.RateScale).IsRequired()
            .HasDefaultValue(ExchangeRates.BaseRate).ValueGeneratedNever();

        builder.HasIndex(x => new { x.OrganizationId, x.AccountId }).IsUnique();

        builder.HasOne<Account>().WithMany().HasForeignKey(x => x.AccountId).OnDelete(DeleteBehavior.Restrict);
    }
}
