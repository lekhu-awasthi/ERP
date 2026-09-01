using ErpApp.Application.Common.Jobs;

namespace ErpApp.Application.Imports;

/// <summary>
/// Owns every decision a bulk import makes; the Infrastructure hosted service that drives it owns
/// only the timer and the per-tick scope. This is the same runner/decider split Phase 20e
/// established with <c>IAlertDispatcher</c>, and it is why this phase's tests need neither a real
/// clock nor a <c>Task.Delay</c>: the whole thing is directly callable with a
/// <c>FakeTimeProvider</c>.
///
/// <para>Phase 21b lifted the two members onto the shared <see cref="IQueuedJobProcessor"/> seam
/// when the export job needed the identical loop. Nothing about the import's behaviour changed;
/// this interface survives as the DI key that <c>QueuedJobRunnerHostedService</c> is closed over,
/// so imports and exports keep separate timers, separate poll intervals and separate kill
/// switches.</para>
/// </summary>
public interface IImportJobProcessor : IQueuedJobProcessor;
