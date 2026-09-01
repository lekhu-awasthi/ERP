namespace ErpApp.Infrastructure.Imports;

/// <summary>
/// Configuration for the import runner's background loop, bound from the "ImportJobRunner" section
/// lazily via AddOptions().Bind() -- never an eager builder.Configuration read, per CLAUDE.md's
/// eager-config-read gotcha. Read through IOptionsMonitor, not IOptions, for the reason recorded in
/// phase-20g: a singleton holding IOptions never sees a later user-secrets change.
/// </summary>
public sealed class ImportJobRunnerOptions
{
    public const string SectionName = "ImportJobRunner";

    /// <summary>Master switch, so a deployment can run the runner on one instance of N, and so a
    /// developer (or an integration test) can keep a background loop off their machine.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// How often the loop looks for queued work. Five seconds rather than the alert scheduler's
    /// sixty, because this is user-initiated: someone is watching a progress bar, and a minute of
    /// "Queued" before anything happens would read as broken. The poll is a single indexed query
    /// when there is nothing to do.
    /// </summary>
    public TimeSpan PollInterval { get; set; } = TimeSpan.FromSeconds(5);
}
