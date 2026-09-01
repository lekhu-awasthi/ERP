namespace ErpApp.Domain.Imports;

/// <summary>
/// One queued bulk-import run (product-requirements.md FR-2.9, NFR-4.3). Tenant-scoped by
/// <see cref="OrganizationId"/> like every other aggregate here -- there is no EF global query
/// filter in this codebase, so every handler filters manually.
///
/// <para><b>Named ImportJob, never Import.</b> <c>PurchaseBill.IsImport</c>/<c>ImportCountry</c>/
/// <c>ImportDate</c> are customs imports, an unrelated domain concept that already owns the bare
/// word in this tree.</para>
///
/// <para><b>Why this table exists at all rather than an in-process queue.</b> A
/// <c>Channel&lt;T&gt;</c> would have been less code, but an import enqueued a second before a
/// deploy would vanish with no trace and no way for the user to learn it never ran. The durable
/// row is also what makes the crash story in <see cref="ImportJobRowStatus"/> expressible.</para>
///
/// <para><b>InitiatedByUserId is the whole of Decision B.</b> It is captured from the real,
/// authenticated, permission-checked HTTP request that enqueued the job, and the runner re-assumes
/// it so the create/update commands travel the normal MediatR pipeline -- meaning permission is
/// re-checked per row at execution time, not merely at enqueue time. See
/// docs/phase-21a-status.md.</para>
/// </summary>
public sealed class ImportJob
{
    public Guid Id { get; private set; }
    public Guid OrganizationId { get; private set; }
    public ImportEntityType EntityType { get; private set; }
    public ImportMode Mode { get; private set; }

    /// <summary>Opaque IFileStorage key for the uploaded workbook (Phase 18's abstraction). Never a
    /// path or a URL -- the file is only ever reopened server-side by the runner.</summary>
    public string StorageKey { get; private set; } = null!;

    /// <summary>The uploader's own file name, kept only so the job list can show what they uploaded.</summary>
    public string FileName { get; private set; } = null!;

    public ImportJobStatus Status { get; private set; }

    /// <summary>Set only when <see cref="Status"/> is <see cref="ImportJobStatus.Failed"/> -- a
    /// whole-file problem (unreadable, wrong columns, empty, permission revoked). A rejected row
    /// never lands here; it lands on its own <see cref="ImportJobRow"/>.</summary>
    public string? FailureReason { get; private set; }

    public Guid InitiatedByUserId { get; private set; }

    public int TotalRowCount { get; private set; }
    public int ProcessedRowCount { get; private set; }
    public int SucceededRowCount { get; private set; }
    public int FailedRowCount { get; private set; }

    /// <summary>User-requested stop. Set by the cancel command from an ordinary HTTP request and
    /// read by the runner between rows -- a running job is never aborted mid-command, because the
    /// command's own transaction is the smallest safe unit.</summary>
    public bool CancellationRequested { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? StartedAt { get; private set; }
    public DateTimeOffset? CompletedAt { get; private set; }

    /// <summary>Liveness stamp refreshed as rows are processed. A <see cref="ImportJobStatus.Running"/>
    /// job whose heartbeat has gone stale is one whose process died; the runner re-claims it and
    /// resumes from the first unclaimed row. Without this a crash would strand a job in Running
    /// forever.</summary>
    ///
    /// <remarks>
    /// <para><b>There is deliberately no concurrency token on this aggregate</b>, and that is a
    /// correction this phase's manual E2E forced. A rowversion here was tried first, to make two
    /// runners unable to claim the same job. It works for claiming and then breaks everything else:
    /// this row has a <i>second</i>, entirely legitimate writer -- the user's cancel command -- and
    /// SQL Server bumps a rowversion on any UPDATE, so a cancel mid-import invalidated the running
    /// job's token and its next progress write died with a DbUpdateConcurrencyException. The job
    /// wedged in Running until its lease expired. (Observed live; no unit test could have found it,
    /// because the InMemory provider does not enforce concurrency tokens at all.)</para>
    ///
    /// <para>Removing it costs nothing, because job-level claiming was never the correctness
    /// mechanism: <see cref="ImportJobRow"/>'s unique index is. Two runners on one job interleave,
    /// each skipping rows the other claimed, and both finalise to the same counts -- duplicated
    /// effort, never a duplicated Product. That is exactly Phase 20e's position, where the
    /// AlertSendLog index (not any lock) is what makes a send happen once.</para>
    /// </remarks>
    public DateTimeOffset? HeartbeatAt { get; private set; }

    private ImportJob()
    {
    }

    public static ImportJob Create(
        Guid organizationId,
        ImportEntityType entityType,
        ImportMode mode,
        string storageKey,
        string fileName,
        Guid initiatedByUserId,
        DateTimeOffset now)
    {
        return new ImportJob
        {
            Id = Guid.NewGuid(),
            OrganizationId = organizationId,
            EntityType = entityType,
            Mode = mode,
            StorageKey = storageKey,
            FileName = fileName,
            InitiatedByUserId = initiatedByUserId,
            Status = ImportJobStatus.Queued,
            CreatedAt = now,
        };
    }

    /// <summary>Takes ownership of a Queued job, or re-takes a Running one whose runner died.</summary>
    public void Claim(DateTimeOffset now)
    {
        Status = ImportJobStatus.Running;
        StartedAt ??= now;
        HeartbeatAt = now;
    }

    public void Heartbeat(DateTimeOffset now) => HeartbeatAt = now;

    public void SetTotalRowCount(int totalRowCount) => TotalRowCount = totalRowCount;

    /// <summary>Counters are recomputed from the ImportJobRows table rather than incremented, so a
    /// resumed job reports the true totals across both runs instead of restarting from zero.</summary>
    public void SetProgress(int processed, int succeeded, int failed)
    {
        ProcessedRowCount = processed;
        SucceededRowCount = succeeded;
        FailedRowCount = failed;
    }

    public void RequestCancellation() => CancellationRequested = true;

    /// <summary>Every row reached a terminal outcome. Reached whether or not any row was rejected --
    /// see <see cref="ImportJobStatus"/> for why partial success is success.</summary>
    public void MarkCompleted(DateTimeOffset now)
    {
        Status = ImportJobStatus.Completed;
        CompletedAt = now;
    }

    public void MarkFailed(DateTimeOffset now, string reason)
    {
        const int MaxReasonLength = 1000;

        Status = ImportJobStatus.Failed;
        FailureReason = reason.Length > MaxReasonLength ? reason[..MaxReasonLength] : reason;
        CompletedAt = now;
    }

    /// <summary>Rows already applied stay applied -- they are real master data another document may
    /// already reference, and silently deleting them would be a worse surprise than stopping where
    /// the user asked. The counts say exactly how far it got.</summary>
    public void MarkCancelled(DateTimeOffset now)
    {
        Status = ImportJobStatus.Cancelled;
        CompletedAt = now;
    }

    public bool IsTerminal =>
        Status is ImportJobStatus.Completed or ImportJobStatus.Failed or ImportJobStatus.Cancelled;
}
