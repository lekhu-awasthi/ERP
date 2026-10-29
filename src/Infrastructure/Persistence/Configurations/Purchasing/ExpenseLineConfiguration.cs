using ErpApp.Domain.Accounting;
using ErpApp.Domain.Purchasing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ErpApp.Infrastructure.Persistence.Configurations.Purchasing;

public sealed class ExpenseLineConfiguration : IEntityTypeConfiguration<ExpenseLine>
{
    public void Configure(EntityTypeBuilder<ExpenseLine> builder)
    {
        builder.ToTable("ExpenseLines", schema: "purchasing");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Amount).HasPrecision(18, 4).IsRequired();
        builder.Property(x => x.VatRate).HasConversion<string>().HasMaxLength(30).IsRequired();
        builder.Property(x => x.VatAmount).HasPrecision(18, 4).IsRequired();

        // Phase 99 -- see PurchaseBillLineConfiguration.
        builder.Property(x => x.CreditUse).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(x => x.CreditCategory).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Ignore(x => x.DisallowedVat);

        builder.HasOne<Account>().WithMany().HasForeignKey(x => x.AccountId).OnDelete(DeleteBehavior.Restrict);
    }
}
