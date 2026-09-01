using ErpApp.Infrastructure.Jobs;

namespace ErpApp.Infrastructure.Exports;

/// <summary>
/// The export runner's own configuration section (roadmap Phase 21b, FR-2.8). Separate from
/// <c>ImportJobRunnerOptions</c> so either runner can be disabled or re-paced without touching the
/// other -- see <c>QueuedJobRunnerHostedService</c> for why they are separate hosted services.
/// </summary>
public sealed class ExportJobRunnerOptions : QueuedJobRunnerOptions
{
    public const string SectionName = "ExportJobRunner";

    /// <summary>Same five seconds as the import runner, and for the same reason: a user is watching
    /// a progress bar.</summary>
    public ExportJobRunnerOptions() => PollInterval = TimeSpan.FromSeconds(5);
}
