using ErpApp.Application.Common.Jobs;

namespace ErpApp.Application.Exports;

/// <summary>
/// Owns every decision a data export makes; the Infrastructure hosted service that drives it owns
/// only the timer and the per-job scope. The same runner/decider split Phase 20e established with
/// <c>IAlertDispatcher</c> and Phase 21a repeated with <c>IImportJobProcessor</c> -- and the reason
/// this phase's tests need neither a real clock nor a <c>Task.Delay</c>.
/// </summary>
public interface IExportJobProcessor : IQueuedJobProcessor;
