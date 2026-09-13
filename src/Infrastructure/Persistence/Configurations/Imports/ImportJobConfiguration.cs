using ErpApp.Domain.Imports;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ErpApp.Infrastructure.Persistence.Configurations.Imports;

public sealed class ImportJobConfiguration : IEntityTypeConfiguration<ImportJob>
{
    public void Configure(EntityTypeBuilder<ImportJob> builder)
    {
        builder.ToTable("ImportJobs", schema: "imports");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.OrganizationId).IsRequired();
        builder.Property(x => x.EntityType).HasConversion<string>().HasMaxLength(40).IsRequired();
        builder.Property(x => x.Mode).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(x => x.StorageKey).HasMaxLength(200).IsRequired();
        builder.Property(x => x.FileName).HasMaxLength(260).IsRequired();
        // 30, not 20: phase 38's "PendingConfirmation" is 19 characters, and a status column one
        // character from overflowing is a truncation waiting for the next member.
        builder.Property(x => x.Status).HasConversion<string>().HasMaxLength(30).IsRequired();
        builder.Property(x => x.FailureReason).HasMaxLength(1000);
        builder.Property(x => x.InitiatedByUserId).IsRequired();
        builder.Property(x => x.CreatedAt).IsRequired();

        // Phase 38. The default is false, and that is a statement about the rows that already
        // exist rather than a convenience: every import that ran before this phase applied without
        // a review, so false is what those rows would say if asked. (A default is safe exactly when
        // it is the truth about the existing rows -- phase 37's rule, and the reason this needed no
        // hand-written backfill while phase 31's DueDate did.)
        builder.Property(x => x.ReviewBeforeApply).HasDefaultValue(false).IsRequired();

        // Note what is NOT here: a rowversion / concurrency token. One was tried and removed --
        // see ImportJob.HeartbeatAt's remarks for the cancel-versus-progress conflict it caused and
        // why ImportJobRow's unique index is the real guarantee.

        // The runner's own claim query: "oldest Queued, or Running with a stale heartbeat".
        builder.HasIndex(x => new { x.Status, x.HeartbeatAt, x.CreatedAt });

        // The screen's own listing (this organization, newest first).
        builder.HasIndex(x => new { x.OrganizationId, x.CreatedAt });
    }
}
