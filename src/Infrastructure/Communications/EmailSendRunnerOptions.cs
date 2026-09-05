using ErpApp.Infrastructure.Jobs;

namespace ErpApp.Infrastructure.Communications;

/// <summary>
/// The email sender's own configuration section (roadmap Phase 30, FR-11.1). Its own section, and
/// its own hosted service, for phase-21b Decision C's reason: a 5,000-row import must not be able to
/// hold up an invoice email, and one registration line is the whole price of avoiding that.
/// </summary>
public sealed class EmailSendRunnerOptions : QueuedJobRunnerOptions
{
    public const string SectionName = "EmailSendRunner";

    /// <summary>Two seconds rather than the import/export runners' five. Nobody watches a progress
    /// bar here — they press Send and expect the mail to be gone. The work per tick when the queue
    /// is empty is one indexed query, so the extra polling costs nothing worth measuring.</summary>
    public EmailSendRunnerOptions() => PollInterval = TimeSpan.FromSeconds(2);
}
