namespace ErpApp.Domain.Communications;

/// <summary>
/// Lifecycle of one <see cref="EmailSendLog"/> row. Four states rather than three because, unlike
/// <c>AlertSendLog</c>, the row is written by an HTTP request and acted on later by a background
/// job (Decision D), so "accepted" and "in the hands of SMTP" are genuinely different moments a
/// user can observe.
/// </summary>
public enum EmailSendStatus
{
    /// <summary>Accepted and committed by the request, not yet picked up. The only state the runner
    /// will claim.</summary>
    Queued,

    /// <summary>Claimed by a runner and handed toward SMTP. <b>A row stuck here is never
    /// re-claimed</b> — see <see cref="EmailSendLog"/> for why that at-most-once choice is the same
    /// one phase 20e made, and why it is even more clearly right here.</summary>
    Sending,

    /// <summary><c>IEmailSender</c> returned without throwing.</summary>
    Sent,

    /// <summary>The send threw, or its attachments could not be assembled. Not retried; a resend is
    /// a new row.</summary>
    Failed,
}
