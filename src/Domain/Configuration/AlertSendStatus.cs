namespace ErpApp.Domain.Configuration;

/// <summary>
/// Lifecycle of one <see cref="AlertSendLog"/> row. The three states exist because the row is
/// written *before* the email is handed to SMTP, not after -- that ordering is what makes the
/// ledger a claim ticket (and therefore the idempotency and multi-instance mechanism), not just a
/// history table. See <see cref="AlertSendLog"/> and docs/phase-20e-status.md Decision C.
/// </summary>
public enum AlertSendStatus
{
    /// <summary>Claimed, not yet confirmed handed to SMTP. A row left in this state is a process
    /// that died mid-send; it is deliberately never retried (at-most-once).</summary>
    Pending,

    /// <summary>IEmailSender.SendAsync returned without throwing.</summary>
    Sent,

    /// <summary>IEmailSender.SendAsync threw. Not retried within this occurrence; the next day's
    /// occurrence is a fresh row.</summary>
    Failed,
}
