using ErpApp.Domain.Accounting;
using ErpApp.Domain.Configuration;
using ErpApp.Domain.Sales;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ErpApp.Infrastructure.Persistence.Configurations.Sales;

/// <summary>Phase 63 -- the mirror of <see cref="InvoiceTenderConfiguration"/>, for the same reasons.</summary>
public sealed class CreditNotePayoutConfiguration : IEntityTypeConfiguration<CreditNotePayout>
{
    public void Configure(EntityTypeBuilder<CreditNotePayout> builder)
    {
        builder.ToTable("CreditNotePayouts", schema: "sales");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Kind).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(x => x.Amount).HasPrecision(18, 4).IsRequired();

        builder.HasOne<PaymentMode>()
            .WithMany()
            .HasForeignKey(x => x.PaymentModeId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<Account>()
            .WithMany()
            .HasForeignKey(x => x.AccountId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
