namespace ErpApp.Application.Imports;

/// <summary>
/// Owns every decision a bulk import makes; the Infrastructure hosted service that drives it owns
/// only the timer and the per-tick scope. This is the same runner/decider split Phase 20e
/// established with <c>IAlertDispatcher</c>, and it is why this phase's tests need neither a real
/// clock nor a <c>Task.Delay</c>: the whole thing is directly callable with a
/// <c>FakeTimeProvider</c>.
/// </summary>
public interface IImportJobProcessor
{
    /// <summary>
    /// Claims and runs at most one job, start to finish, then returns. Returns false when there was
    /// nothing to do.
    ///
    /// <para>One job per call, run to completion synchronously, is deliberate: imports are
    /// sequential by nature (row 40 may depend on a category row 3 created) and running two at once
    /// would buy nothing but contention. A long import simply occupies the runner; the timer's next
    /// tick finds it still busy and does nothing.</para>
    /// </summary>
    Task<bool> ProcessNextAsync(CancellationToken cancellationToken);
}
