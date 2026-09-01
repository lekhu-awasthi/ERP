using ErpApp.Infrastructure.Jobs;

namespace ErpApp.Infrastructure.Imports;

/// <summary>
/// The import runner's own configuration section. Phase 21b moved the two properties up to
/// <see cref="QueuedJobRunnerOptions"/> when the export runner needed the same pair; the section
/// name and both defaults are unchanged, so no deployed configuration key moves.
/// </summary>
public sealed class ImportJobRunnerOptions : QueuedJobRunnerOptions
{
    public const string SectionName = "ImportJobRunner";

    /// <summary>
    /// Five seconds rather than the alert scheduler's sixty, because this is user-initiated:
    /// someone is watching a progress bar, and a minute of "Queued" before anything happens would
    /// read as broken.
    /// </summary>
    public ImportJobRunnerOptions() => PollInterval = TimeSpan.FromSeconds(5);
}
