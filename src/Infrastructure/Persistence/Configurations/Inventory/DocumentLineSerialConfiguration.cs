using ErpApp.Domain.Inventory;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ErpApp.Infrastructure.Persistence.Configurations.Inventory;

public sealed class DocumentLineSerialConfiguration : IEntityTypeConfiguration<DocumentLineSerial>
{
    public void Configure(EntityTypeBuilder<DocumentLineSerial> builder)
    {
        builder.ToTable("DocumentLineSerials", schema: "inventory");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.OrganizationId).IsRequired();
        builder.Property(x => x.ParentType).HasConversion<string>().HasMaxLength(30).IsRequired();
        builder.Property(x => x.ParentLineId).IsRequired();
        builder.Property(x => x.SerialNo).HasMaxLength(DocumentLineSerial.SerialNoMaxLength).IsRequired();
        builder.Property(x => x.CreatedAt).IsRequired();

        // No FK: the parent is one of four line tables, chosen by ParentType -- the same polymorphic
        // shape as Comment/Attachment/WorkTask, and the same reason none of those carries one.

        // The read path: the serials of one line, which is every read this table has.
        builder.HasIndex(x => new { x.OrganizationId, x.ParentType, x.ParentLineId });

        // One line may not name the same serial twice. The in-line duplicate check in
        // StockTrackingRules gives a 400 that names the number; this is the backstop that makes it
        // an invariant rather than a validation.
        builder.HasIndex(x => new { x.OrganizationId, x.ParentType, x.ParentLineId, x.SerialNo }).IsUnique();
    }
}
