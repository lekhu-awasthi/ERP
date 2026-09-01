namespace ErpApp.Application.Common.Jobs;

/// <summary>
/// How long a background job's file on <c>IFileStorage</c> lives before the owning processor's
/// <c>SweepAsync</c> deletes it (docs/phase-21b-status.md, Decision E).
///
/// <para><b>Retention is this phase's own consequence, not a nice-to-have.</b> Before Phase 21b
/// exactly one caller in the whole tree ever invoked <c>IFileStorage.DeleteAsync</c>
/// (<c>DeleteAttachmentCommandHandler</c>), so every workbook uploaded to Phase 21a's importer
/// leaked permanently. An export leaks something far larger and produced far more casually -- the
/// tenant's entire product, contact, account, ledger and stock data in one file, regenerable in two
/// clicks -- so shipping it without a deletion story would have compounded a known leak rather than
/// fixing one. Both sweeps run here.</para>
///
/// <para>A constant rather than bound options, deliberately: Application takes no dependency on
/// <c>Microsoft.Extensions.Options</c> anywhere else in this codebase, and a retention window is a
/// product decision this phase is making, not a per-deployment knob anybody has asked for. Moving
/// it to configuration later is a one-line change in each processor's constructor.</para>
/// </summary>
public static class JobArtifactRetention
{
    /// <summary>
    /// Seven days. Short enough that a full-tenant dump is not sitting on disk indefinitely -- its
    /// lifetime is a security posture, not housekeeping -- and long enough that someone who
    /// generates an export on Friday can still fetch it on Monday. Regenerating is idempotent and
    /// cheap, which is what makes a short window affordable.
    /// </summary>
    public static readonly TimeSpan Period = TimeSpan.FromDays(7);

    /// <summary>Rows a single sweep will purge, so one tick cannot turn into an unbounded delete
    /// loop against storage. The next tick picks up whatever is left.</summary>
    public const int SweepBatchSize = 100;
}
