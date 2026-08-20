using ErpApp.Domain.Payments;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ErpApp.Infrastructure.Persistence.Configurations.Payments;

public sealed class PaymentAllocationConfiguration : IEntityTypeConfiguration<PaymentAllocation>
{
    public void Configure(EntityTypeBuilder<PaymentAllocation> builder)
    {
        builder.ToTable("PaymentAllocations", schema: "payments");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.SourceType).HasConversion<string>().HasMaxLength(30).IsRequired();
        builder.Property(x => x.SourceId).IsRequired();
        builder.Property(x => x.TargetDocumentType).HasConversion<string>().HasMaxLength(30).IsRequired();
        builder.Property(x => x.TargetDocumentId).IsRequired();
        builder.Property(x => x.Amount).HasPrecision(18, 4).IsRequired();

        // No FK on either polymorphic side -- SourceType/SourceId names rows in either Payments or
        // JournalVoucherLines depending on SourceType (docs/phase-17-status.md decision #2), same
        // "indexed, not FK-constrained" treatment TargetDocumentType/TargetDocumentId already used
        // for the Target side (architecture-spec.md §3.4).
        builder.HasIndex(x => new { x.SourceType, x.SourceId });
        builder.HasIndex(x => new { x.TargetDocumentType, x.TargetDocumentId });
    }
}
