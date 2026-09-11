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

        // Phase 35b -- nullable, and deliberately un-indexed. Every GL report applies this filter
        // *alongside* its period, so the seek is the (OrganizationId, PostedAt) index
        // TenantIndexConvention already derives for this table and the location is a residual
        // predicate over a set the date range has already narrowed. A composite leading with
        // LocationId would change the plan for every unfiltered GL report over the same table --
        // exactly the regression phase 34c measured when one index added for one access path made a
        // non-matching search 1.8x slower (docs/phase-34c-status.md, Decision C).
        builder.Property(x => x.LocationId);

        builder.HasIndex(x => new { x.SourceDocumentType, x.SourceDocumentId });

        builder.HasMany(x => x.Lines)
            .WithOne()
            .HasForeignKey("GlJournalEntryId")
            .OnDelete(DeleteBehavior.Cascade);

        builder.Metadata.FindNavigation(nameof(GlJournalEntry.Lines))!
            .SetPropertyAccessMode(PropertyAccessMode.Field);
    }
}
