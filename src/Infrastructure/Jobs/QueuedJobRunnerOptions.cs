namespace ErpApp.Infrastructure.Jobs;

/// <summary>
/// The knobs every queued-job runner has. Bound lazily via <c>AddOptions().Bind()</c> -- never an
/// eager <c>builder.Configuration</c> read, per CLAUDE.md's eager-config-read gotcha -- and read
/// through <c>IOptionsMonitor</c>, not <c>IOptions</c>, for the reason recorded in phase-20g: a
/// singleton holding <c>IOptions</c> never sees a later user-secrets change.
///
/// <para>Each runner gets its own concrete subclass with its own configuration section, rather than
/// one shared section with a nested map. That keeps <c>ImportJobRunner</c>'s existing key stable
/// (Phase 21a shipped it) and lets exports poll on their own cadence.</para>
/// </summary>
public abstract class QueuedJobRunnerOptions
{
    /// <summary>Master switch, so a deployment can run a given runner on one instance of N, and so a
    /// developer (or an integration test) can keep a background loop off their machine.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>How often the loop looks for queued work. The poll is a single indexed query when
    /// there is nothing to do.</summary>
    public TimeSpan PollInterval { get; set; } = TimeSpan.FromSeconds(5);
}
