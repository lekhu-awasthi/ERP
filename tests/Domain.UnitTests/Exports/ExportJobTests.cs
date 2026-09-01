using ErpApp.Domain.Exports;

namespace ErpApp.Domain.UnitTests.Exports;

/// <summary>
/// <see cref="ExportJob"/>'s own invariants. The one that matters most is
/// <see cref="ExportJob.HasArtifact"/>: it is what the UI's Download button and the download
/// endpoint both key off, so "completed but purged" and "running with nothing produced yet" must
/// both read as no-artifact rather than as a dead link.
/// </summary>
public class ExportJobTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 1, 10, 0, 0, TimeSpan.Zero);
    private static readonly TimeSpan Retention = TimeSpan.FromDays(7);

    [Fact]
    public void A_new_job_is_queued_with_no_artifact()
    {
        var job = ExportJob.Create(Guid.NewGuid(), Guid.NewGuid(), 5, Now);

        Assert.Equal(ExportJobStatus.Queued, job.Status);
        Assert.False(job.IsTerminal);
        Assert.False(job.HasArtifact);
        Assert.Null(job.StorageKey);
        Assert.Null(job.ExpiresAt);
        Assert.Equal(5, job.TotalCategoryCount);
    }

    [Fact]
    public void Completing_records_the_artifact_and_when_it_expires()
    {
        var job = ExportJob.Create(Guid.NewGuid(), Guid.NewGuid(), 5, Now);
        job.Claim(Now);
        job.MarkCompleted(Now, "key-1", "DataExport.xlsx", 4096, null, Retention);

        Assert.Equal(ExportJobStatus.Completed, job.Status);
        Assert.True(job.IsTerminal);
        Assert.True(job.HasArtifact);
        Assert.Equal("key-1", job.StorageKey);
        Assert.Equal(4096, job.FileSizeBytes);
        Assert.Equal(Now + Retention, job.ExpiresAt);
    }

    /// <summary>Retention clears the key so nothing can hand out an identifier that no longer
    /// resolves, but keeps the row and its file name so the history can say what expired.</summary>
    [Fact]
    public void Purging_clears_the_key_but_keeps_the_row_readable()
    {
        var job = ExportJob.Create(Guid.NewGuid(), Guid.NewGuid(), 5, Now);
        job.Claim(Now);
        job.MarkCompleted(Now, "key-1", "DataExport.xlsx", 4096, null, Retention);

        var purgedAt = Now + Retention + TimeSpan.FromHours(1);
        job.MarkArtifactPurged(purgedAt);

        Assert.False(job.HasArtifact);
        Assert.Null(job.StorageKey);
        Assert.Equal(purgedAt, job.ArtifactPurgedAt);
        Assert.Equal(ExportJobStatus.Completed, job.Status);
        Assert.Equal("DataExport.xlsx", job.FileName);
    }

    /// <summary>Re-claiming an abandoned run rebuilds the workbook from scratch, so its progress
    /// must restart from zero rather than resuming a count from a process that is gone.</summary>
    [Fact]
    public void Reclaiming_an_abandoned_job_resets_progress_but_keeps_the_original_start()
    {
        var job = ExportJob.Create(Guid.NewGuid(), Guid.NewGuid(), 5, Now);
        job.Claim(Now);
        job.SetProgress(3, 120, Now + TimeSpan.FromSeconds(30));

        var later = Now + TimeSpan.FromMinutes(10);
        job.Claim(later);

        Assert.Equal(0, job.ProcessedCategoryCount);
        Assert.Equal(0, job.TotalRowCount);
        Assert.Equal(Now, job.StartedAt);
        Assert.Equal(later, job.HeartbeatAt);
    }

    [Fact]
    public void A_cancelled_job_is_terminal_and_has_nothing_to_download()
    {
        var job = ExportJob.Create(Guid.NewGuid(), Guid.NewGuid(), 5, Now);
        job.Claim(Now);
        job.RequestCancellation();
        job.MarkCancelled(Now);

        Assert.True(job.CancellationRequested);
        Assert.Equal(ExportJobStatus.Cancelled, job.Status);
        Assert.True(job.IsTerminal);
        Assert.False(job.HasArtifact);
    }

    [Fact]
    public void A_failure_reason_is_truncated_rather_than_overflowing_its_column()
    {
        var job = ExportJob.Create(Guid.NewGuid(), Guid.NewGuid(), 5, Now);
        job.MarkFailed(Now, new string('x', 5000));

        Assert.Equal(1000, job.FailureReason!.Length);
    }

    [Fact]
    public void A_truncation_notice_is_truncated_too()
    {
        var job = ExportJob.Create(Guid.NewGuid(), Guid.NewGuid(), 5, Now);
        job.MarkCompleted(Now, "key-1", "f.xlsx", 1, new string('y', 5000), Retention);

        Assert.Equal(1000, job.TruncationNotice!.Length);
    }
}
