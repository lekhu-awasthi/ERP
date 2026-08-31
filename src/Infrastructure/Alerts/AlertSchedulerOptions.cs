namespace ErpApp.Infrastructure.Alerts;

/// <summary>
/// Configuration for the alert scheduler's background loop, bound from the "AlertScheduler" section
/// (appsettings / user-secrets / environment), lazily via AddOptions().Bind() -- never an eager
/// builder.Configuration read, per CLAUDE.md's eager-config-read gotcha.
/// </summary>
public sealed class AlertSchedulerOptions
{
    public const string SectionName = "AlertScheduler";

    /// <summary>
    /// Master switch. Defaults to enabled, but exists so a deployment can run N app instances with
    /// the scheduler on exactly one of them if it prefers that to relying on the send ledger's
    /// unique index (both are safe; see docs/phase-20e-status.md's multi-instance position). It is
    /// also what integration tests and a developer who does not want a mail loop on their laptop
    /// turn off.
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// How often the loop wakes and asks the dispatcher what is due. Sixty seconds is chosen to
    /// match the resolution of the product's own time picker (HH:mm) -- polling faster cannot make
    /// an alert fire earlier than its minute, and polling slower would make a 19:57 alert arrive at
    /// an arbitrary later minute. The poll is a single indexed query when nothing is due.
    ///
    /// <para>Shortening this is also how manual E2E is done, since the reference product has no
    /// "Run now" action to copy (confirmed live -- the row menu is Edit / Delete / Mark As Inactive
    /// and nothing else).</para>
    /// </summary>
    public TimeSpan PollInterval { get; set; } = TimeSpan.FromSeconds(60);
}
