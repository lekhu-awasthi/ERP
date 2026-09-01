using ErpApp.Domain.Imports;

namespace ErpApp.Domain.UnitTests.Imports;

/// <summary>
/// The aggregate's own invariants, independent of the runner. The lifecycle assertions here are the
/// domain half of Decision C: a rejected row must never be able to make the <i>job</i> Failed, and a
/// cancelled job must keep the counts that say how far it actually got.
/// </summary>
public class ImportJobTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 1, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Create_starts_queued_with_no_progress_and_no_completion()
    {
        var job = NewJob();

        Assert.Equal(ImportJobStatus.Queued, job.Status);
        Assert.False(job.IsTerminal);
        Assert.False(job.CancellationRequested);
        Assert.Equal(0, job.TotalRowCount);
        Assert.Null(job.StartedAt);
        Assert.Null(job.CompletedAt);
        Assert.Null(job.HeartbeatAt);
    }

    [Fact]
    public void Claim_starts_the_clock_once_and_a_resumed_claim_does_not_reset_it()
    {
        var job = NewJob();

        job.Claim(Now);
        var firstStart = job.StartedAt;

        job.Claim(Now.AddMinutes(10));

        Assert.Equal(ImportJobStatus.Running, job.Status);
        Assert.Equal(firstStart, job.StartedAt);
        Assert.Equal(Now.AddMinutes(10), job.HeartbeatAt);
    }

    /// <summary>Partial success is success -- see ImportJobStatus for why Failed has to mean
    /// something narrower than "a row was rejected".</summary>
    [Fact]
    public void A_job_with_rejected_rows_still_completes()
    {
        var job = NewJob();
        job.Claim(Now);
        job.SetTotalRowCount(1000);
        job.SetProgress(processed: 1000, succeeded: 997, failed: 3);

        job.MarkCompleted(Now.AddMinutes(1));

        Assert.Equal(ImportJobStatus.Completed, job.Status);
        Assert.True(job.IsTerminal);
        Assert.Equal(3, job.FailedRowCount);
        Assert.Equal(997, job.SucceededRowCount);
    }

    [Fact]
    public void MarkCancelled_keeps_the_counts_that_say_how_far_it_got()
    {
        var job = NewJob();
        job.Claim(Now);
        job.SetTotalRowCount(500);
        job.SetProgress(processed: 120, succeeded: 118, failed: 2);
        job.RequestCancellation();

        job.MarkCancelled(Now.AddMinutes(2));

        Assert.Equal(ImportJobStatus.Cancelled, job.Status);
        Assert.True(job.IsTerminal);
        Assert.Equal(120, job.ProcessedRowCount);
        Assert.Equal(118, job.SucceededRowCount);
        Assert.Equal(Now.AddMinutes(2), job.CompletedAt);
    }

    /// <summary>A failure reason can carry an arbitrary provider message; truncating it must never be
    /// able to fail the save that records the failure.</summary>
    [Fact]
    public void MarkFailed_truncates_an_overlong_reason_rather_than_rejecting_it()
    {
        var job = NewJob();

        job.MarkFailed(Now, new string('x', 5000));

        Assert.Equal(ImportJobStatus.Failed, job.Status);
        Assert.Equal(1000, job.FailureReason!.Length);
    }

    private static ImportJob NewJob() => ImportJob.Create(
        Guid.NewGuid(),
        ImportEntityType.Product,
        ImportMode.CreateNew,
        "storage-key",
        "products.xlsx",
        Guid.NewGuid(),
        Now);
}

public class ImportJobRowTests
{
    [Fact]
    public void Claim_starts_pending_which_is_the_claim_not_a_result()
    {
        var row = ImportJobRow.Claim(Guid.NewGuid(), Guid.NewGuid(), 7);

        Assert.Equal(ImportJobRowStatus.Pending, row.Status);
        Assert.Equal(7, row.RowNumber);
        Assert.Null(row.TargetId);
        Assert.Null(row.Message);
    }

    [Fact]
    public void MarkSucceeded_records_the_target_and_clears_any_earlier_message()
    {
        var row = ImportJobRow.Claim(Guid.NewGuid(), Guid.NewGuid(), 2);
        row.MarkFailed("Category", "does not exist");

        var targetId = Guid.NewGuid();
        row.MarkSucceeded(targetId, "PRD-0001");

        Assert.Equal(ImportJobRowStatus.Succeeded, row.Status);
        Assert.Equal(targetId, row.TargetId);
        Assert.Equal("PRD-0001", row.TargetCode);
        Assert.Null(row.ColumnName);
        Assert.Null(row.Message);
    }

    [Fact]
    public void MarkFailed_truncates_an_overlong_message_and_column_name()
    {
        var row = ImportJobRow.Claim(Guid.NewGuid(), Guid.NewGuid(), 2);

        row.MarkFailed(new string('c', 400), new string('m', 4000));

        Assert.Equal(100, row.ColumnName!.Length);
        Assert.Equal(1000, row.Message!.Length);
    }
}
