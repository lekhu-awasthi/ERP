namespace ErpApp.Domain.Exports;

/// <summary>
/// One queued full-tenant data export (product-requirements.md FR-2.8, NFR-4.3). Tenant-scoped by
/// <see cref="OrganizationId"/> like every other aggregate here -- there is no EF global query
/// filter in this codebase, so every handler and every category reader filters manually.
///
/// <para><b>Why its own table rather than a JobKind column on ImportJob</b> (Decision C). The two
/// share a lifecycle and nothing else. An <c>ImportJob</c> <i>consumes</i> a payload whose
/// <c>StorageKey</c> is set at creation and never changes; this <i>produces</i> one, so
/// <see cref="StorageKey"/> is null until the job finishes and null again once retention deletes
/// it. <c>ImportJob.EntityType</c>/<c>Mode</c> have no export meaning, and the entire
/// <c>ImportJobRow</c> claim-under-a-unique-index machinery exists because an import is not
/// idempotent -- an export is, so re-running one simply regenerates a file. Folding the two
/// together would have meant a table where half the columns are always null on half the rows, plus
/// a migration against a table shipped one phase earlier. What <i>is</i> shared -- the timer, the
/// per-job scope, the drain loop -- is shared, through
/// <c>QueuedJobRunnerHostedService&lt;TProcessor, TOptions&gt;</c>.
/// </para>
///
/// <para><b>There is deliberately no acting-user identity here at run time</b>, unlike
/// <c>ImportJob.InitiatedByUserId</c>'s role in Phase 21a. An export only reads, and it reads
/// directly through org-filtered queries rather than through permission-gated MediatR requests, so
/// Phase 20e's "a background job needs no identity" default holds. <see cref="InitiatedByUserId"/>
/// is recorded for attribution and for the completion email, not to authorize anything -- the
/// permission check and the <c>Audit</c> row both happen on the real HTTP request that enqueued the
/// job. See docs/phase-21b-status.md, Decision D.
/// </para>
/// </summary>
public sealed class ExportJob
{
    public Guid Id { get; private set; }
    public Guid OrganizationId { get; private set; }

    public ExportJobStatus Status { get; private set; }

    /// <summary>Set only when <see cref="Status"/> is <see cref="ExportJobStatus.Failed"/>. An
    /// export has no row-level outcomes, so every failure is a whole-job failure.</summary>
    public string? FailureReason { get; private set; }

    public Guid InitiatedByUserId { get; private set; }

    /// <summary>Opaque IFileStorage key for the produced workbook. <b>Null until the job completes</b>
    /// -- the blob is saved and the key committed in the same step that marks the job Completed, so
    /// a run that dies mid-write can never leave a half-written file the UI offers as a finished
    /// download. Null again after <see cref="ArtifactPurgedAt"/>.</summary>
    public string? StorageKey { get; private set; }

    /// <summary>The download file name presented to the browser. Kept even after the artifact is
    /// purged, so the history grid can still say what the export was.</summary>
    public string? FileName { get; private set; }

    public long? FileSizeBytes { get; private set; }

    public int TotalCategoryCount { get; private set; }
    public int ProcessedCategoryCount { get; private set; }
    public int TotalRowCount { get; private set; }

    /// <summary>Human-readable, comma-separated list of categories cut off at the row cap, e.g.
    /// "Ledger Transactions (25,000 of 41,233 rows)". Null when nothing was truncated. Surfaced in
    /// the grid, on the workbook's Summary sheet and in the completion email -- see
    /// <see cref="ExportJobStatus"/> for why truncation is disclosed rather than made a status.</summary>
    public string? TruncationNotice { get; private set; }

    /// <summary>User-requested stop, read by the runner between categories. A cancelled export
    /// leaves no artifact at all, which is the one place cancellation is <i>simpler</i> here than in
    /// Phase 21a: an import's cancelled rows are already-created master data that must stay.</summary>
    public bool CancellationRequested { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? StartedAt { get; private set; }
    public DateTimeOffset? CompletedAt { get; private set; }

    /// <summary>When the produced artifact becomes eligible for deletion (Decision E). Set at
    /// completion; null on a job that never produced one.</summary>
    public DateTimeOffset? ExpiresAt { get; private set; }

    /// <summary>When retention actually deleted the artifact. The job row survives -- a user who
    /// comes back to a week-old export must be told it expired, not offered a download that 404s.</summary>
    public DateTimeOffset? ArtifactPurgedAt { get; private set; }

    /// <summary>Liveness stamp, refreshed between categories. A Running job whose heartbeat has gone
    /// stale is one whose process died; another runner re-claims it and <b>regenerates the whole
    /// workbook from scratch</b>. That is safe precisely because an export is idempotent -- it is
    /// the entire reason this aggregate needs none of Phase 21a's per-row ledger.</summary>
    ///
    /// <remarks>No concurrency token here, for the same reason ImportJob has none: this row has a
    /// second legitimate writer (the user's cancel command), and SQL Server bumps a rowversion on
    /// any UPDATE, so a cancel mid-run would invalidate the runner's token and wedge its next
    /// progress write. See ImportJob.HeartbeatAt's remarks and phase-21a-status.md's Bug 1. Two
    /// runners racing on one export duplicate effort and produce one file each; the second commit
    /// wins and the first blob is orphaned, which is a cost, not a correctness failure.</remarks>
    public DateTimeOffset? HeartbeatAt { get; private set; }

    private ExportJob()
    {
    }

    public static ExportJob Create(
        Guid organizationId, Guid initiatedByUserId, int totalCategoryCount, DateTimeOffset now)
    {
        return new ExportJob
        {
            Id = Guid.NewGuid(),
            OrganizationId = organizationId,
            InitiatedByUserId = initiatedByUserId,
            TotalCategoryCount = totalCategoryCount,
            Status = ExportJobStatus.Queued,
            CreatedAt = now,
        };
    }

    /// <summary>Takes ownership of a Queued job, or re-takes a Running one whose runner died.
    /// Progress restarts at zero because the workbook is rebuilt from scratch.</summary>
    public void Claim(DateTimeOffset now)
    {
        Status = ExportJobStatus.Running;
        StartedAt ??= now;
        HeartbeatAt = now;
        ProcessedCategoryCount = 0;
        TotalRowCount = 0;
    }

    public void Heartbeat(DateTimeOffset now) => HeartbeatAt = now;

    public void SetProgress(int processedCategoryCount, int totalRowCount, DateTimeOffset now)
    {
        ProcessedCategoryCount = processedCategoryCount;
        TotalRowCount = totalRowCount;
        HeartbeatAt = now;
    }

    public void RequestCancellation() => CancellationRequested = true;

    /// <summary>The artifact and the terminal status land together, in one save -- see
    /// <see cref="StorageKey"/> for why they must not be separable.</summary>
    public void MarkCompleted(
        DateTimeOffset now,
        string storageKey,
        string fileName,
        long fileSizeBytes,
        string? truncationNotice,
        TimeSpan retention)
    {
        const int MaxNoticeLength = 1000;

        Status = ExportJobStatus.Completed;
        StorageKey = storageKey;
        FileName = fileName;
        FileSizeBytes = fileSizeBytes;
        TruncationNotice = truncationNotice is { Length: > MaxNoticeLength }
            ? truncationNotice[..MaxNoticeLength]
            : truncationNotice;
        CompletedAt = now;
        ExpiresAt = now + retention;
    }

    public void MarkFailed(DateTimeOffset now, string reason)
    {
        const int MaxReasonLength = 1000;

        Status = ExportJobStatus.Failed;
        FailureReason = reason.Length > MaxReasonLength ? reason[..MaxReasonLength] : reason;
        CompletedAt = now;
    }

    public void MarkCancelled(DateTimeOffset now)
    {
        Status = ExportJobStatus.Cancelled;
        CompletedAt = now;
    }

    /// <summary>Retention deleted the blob. The key is cleared rather than kept, so nothing can hand
    /// out a storage key that no longer resolves, and so the sweep cannot pick the same row twice.</summary>
    public void MarkArtifactPurged(DateTimeOffset now)
    {
        StorageKey = null;
        ArtifactPurgedAt = now;
    }

    public bool IsTerminal =>
        Status is ExportJobStatus.Completed or ExportJobStatus.Failed or ExportJobStatus.Cancelled;

    /// <summary>True only while a downloadable file genuinely exists on storage.</summary>
    public bool HasArtifact => StorageKey is not null && ArtifactPurgedAt is null;
}
