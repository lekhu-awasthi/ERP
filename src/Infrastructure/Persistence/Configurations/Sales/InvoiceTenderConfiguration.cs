using ErpApp.Domain.Accounting;
using ErpApp.Domain.Configuration;
using ErpApp.Domain.Sales;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ErpApp.Infrastructure.Persistence.Configurations.Sales;

public sealed class InvoiceTenderConfiguration : IEntityTypeConfiguration<InvoiceTender>
{
    public void Configure(EntityTypeBuilder<InvoiceTender> builder)
    {
        builder.ToTable("InvoiceTenders", schema: "sales");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Kind).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(x => x.Amount).HasPrecision(18, 4).IsRequired();

        // Restrict, not cascade: a payment mode a sale was paid in cannot be deleted out from under
        // it. Phase 60 made unlinking it from a till cascade, which is right there -- an unlinked
        // mode just stops being offered -- and is a different relationship.
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
