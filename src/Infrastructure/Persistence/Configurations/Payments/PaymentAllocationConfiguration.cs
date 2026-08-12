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

        builder.Property(x => x.TargetDocumentType).HasConversion<string>().HasMaxLength(30).IsRequired();
        builder.Property(x => x.TargetDocumentId).IsRequired();
        builder.Property(x => x.Amount).HasPrecision(18, 4).IsRequired();

        // No FK to a specific target table -- TargetDocumentType/TargetDocumentId is a
        // polymorphic reference (architecture-spec.md §3.4), same reasoning as
        // GlJournalEntry.SourceDocumentType/Id (indexed, not FK-constrained).
        builder.HasIndex(x => new { x.TargetDocumentType, x.TargetDocumentId });
    }
}
