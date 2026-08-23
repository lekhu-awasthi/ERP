using ErpApp.Domain.Configuration;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ErpApp.Infrastructure.Persistence.Configurations.Configuration;

public sealed class TransactionReportingTagConfiguration : IEntityTypeConfiguration<TransactionReportingTag>
{
    public void Configure(EntityTypeBuilder<TransactionReportingTag> builder)
    {
        builder.ToTable("TransactionReportingTags", schema: "configuration");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.OrganizationId).IsRequired();
        // DocumentType/DocumentId are a polymorphic pair, not a real FK -- see the entity's own
        // doc comment, mirroring AttachmentConfiguration/WorkTaskConfiguration.
        builder.Property(x => x.DocumentType).HasConversion<string>().HasMaxLength(30).IsRequired();
        builder.Property(x => x.DocumentId).IsRequired();
        builder.Property(x => x.TagOptionId).IsRequired();
        builder.Property(x => x.CreatedAt).IsRequired();

        builder.HasIndex(x => new { x.OrganizationId, x.DocumentType, x.DocumentId });
        builder.HasIndex(x => new { x.OrganizationId, x.TagOptionId });
        builder.HasIndex(x => new { x.DocumentType, x.DocumentId, x.TagOptionId }).IsUnique();

        builder.HasOne<ReportingTagOption>().WithMany().HasForeignKey(x => x.TagOptionId).OnDelete(DeleteBehavior.Cascade);
    }
}
