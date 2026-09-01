namespace ErpApp.Application.Common.Jobs;

/// <summary>
/// The seam between a background job's <i>decider</i> (an Application-layer processor, holding every
/// decision behind an injected <c>TimeProvider</c>) and its <i>runner</i> (an Infrastructure
/// <c>BackgroundService</c>, holding only the timer and the per-job DI scope). Phase 20e established
/// the split with <c>IAlertDispatcher</c>; Phase 21a repeated it with <c>IImportJobProcessor</c>;
/// this interface is what Phase 21b factored out of the second and third so
/// <c>QueuedJobRunnerHostedService&lt;TProcessor, TOptions&gt;</c> can drive both.
///
/// <para><b>This is not a generic job framework, and the distinction is the point</b>
/// (docs/phase-21b-status.md, Decision C). There is no job-kind discriminator, no shared table, no
/// handler registry, and no dispatch: <c>ImportJob</c> and <c>ExportJob</c> keep their own tables,
/// their own semantics and their own processors, and nothing here knows either exists. What is
/// shared is exactly the part that holds no business decision -- a periodic timer, a scope per job,
/// a drain loop, and the three ways a hosted service goes wrong. Phase 21a declined to generalize
/// because there was only one consumer to look at and the alert scheduler's shape genuinely differs
/// (schedule-driven, idempotent, "what is due now"). Imports and exports are both queue-driven,
/// user-initiated, cancellable and drainable -- the same loop, verbatim -- which is a different
/// situation, and the reason this exists now and did not then. The alert scheduler is deliberately
/// left alone.</para>
/// </summary>
public interface IQueuedJobProcessor
{
    /// <summary>
    /// Claims and runs at most one job, start to finish, then returns. Returns false when there was
    /// nothing to do, which is what stops the runner's drain loop.
    ///
    /// <para>One job per call, run to completion, is deliberate for both current consumers: an
    /// import's row 40 may depend on a category its row 3 created, and an export's peak memory is a
    /// whole buffered workbook. Running two at once would buy nothing but contention.</para>
    /// </summary>
    Task<bool> ProcessNextAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Housekeeping the runner calls once per tick, before draining. Default is a no-op, so a
    /// processor with nothing to sweep says nothing.
    ///
    /// <para>This exists because Phase 21b's retention (Decision E) needs somewhere to live and a
    /// <i>third</i> background service for one indexed DELETE query would be absurd. One extra
    /// indexed query per tick costs the same as the poll that is already happening; a sweep that
    /// finds nothing returns immediately.</para>
    /// </summary>
    Task SweepAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
