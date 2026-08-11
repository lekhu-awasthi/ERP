using ErpApp.Domain.Accounting;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ErpApp.Infrastructure.Persistence.Configurations.Accounting;

public sealed class GlJournalEntryConfiguration : IEntityTypeConfiguration<GlJournalEntry>
{
    public void Configure(EntityTypeBuilder<GlJournalEntry> builder)
    {
        builder.ToTable("GlJournalEntries", schema: "accounting");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.OrganizationId).IsRequired();
        builder.Property(x => x.SourceDocumentType).HasConversion<string>().HasMaxLength(30).IsRequired();
        builder.Property(x => x.SourceDocumentId).IsRequired();
        builder.Property(x => x.PostedAt).IsRequired();

        builder.HasIndex(x => new { x.SourceDocumentType, x.SourceDocumentId });

        builder.HasMany(x => x.Lines)
            .WithOne()
            .HasForeignKey("GlJournalEntryId")
            .OnDelete(DeleteBehavior.Cascade);

        builder.Metadata.FindNavigation(nameof(GlJournalEntry.Lines))!
            .SetPropertyAccessMode(PropertyAccessMode.Field);
    }
}
