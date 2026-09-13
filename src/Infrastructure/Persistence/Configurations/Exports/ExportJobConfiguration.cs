using ErpApp.Domain.Exports;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ErpApp.Infrastructure.Persistence.Configurations.Exports;

public sealed class ExportJobConfiguration : IEntityTypeConfiguration<ExportJob>
{
    public void Configure(EntityTypeBuilder<ExportJob> builder)
    {
        builder.ToTable("ExportJobs", schema: "exports");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.OrganizationId).IsRequired();
        builder.Property(x => x.Status).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(x => x.FailureReason).HasMaxLength(1000);
        builder.Property(x => x.TruncationNotice).HasMaxLength(1000);
        builder.Property(x => x.StorageKey).HasMaxLength(200);
        builder.Property(x => x.FileName).HasMaxLength(260);
        builder.Property(x => x.InitiatedByUserId).IsRequired();
        builder.Property(x => x.CreatedAt).IsRequired();

        // Phase 38. A comma-joined list of ExportCategory names rather than a child table: the set
        // is small, fixed and never queried across rows -- nothing asks "which exports included
        // Payments?" -- so a table would buy a join and cost a migration for nothing.
        //
        // The empty-string default is the truth about the rows that already exist (phase 37's rule):
        // every export written before this phase ran every category, and ExportJob.SelectedCategories
        // reads empty as "all of them" for exactly that reason. 200 characters is roughly six times
        // the longest possible full selection.
        builder.Property(x => x.Categories).HasMaxLength(200).HasDefaultValue(string.Empty).IsRequired();

        // Note what is NOT here, exactly as on ImportJobs: a rowversion / concurrency token. This
        // row has two legitimate writers (the runner's progress writes and the user's cancel), and
        // a token would make the second wedge the first -- phase-21a-status.md's Bug 1.

        // The runner's own claim query: "oldest Queued, or Running with a stale heartbeat".
        builder.HasIndex(x => new { x.Status, x.HeartbeatAt, x.CreatedAt });

        // The retention sweep: "not yet purged, expired before now" (Decision E).
        builder.HasIndex(x => new { x.ArtifactPurgedAt, x.ExpiresAt });

        // The screen's own listing (this organization, newest first).
        builder.HasIndex(x => new { x.OrganizationId, x.CreatedAt });
    }
}
