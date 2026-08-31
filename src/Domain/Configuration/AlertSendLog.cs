namespace ErpApp.Domain.Configuration;

/// <summary>
/// One row per (alert definition, local occurrence date, recipient). Backs the reference product's
/// "Email Logs" panel, found live during Phase 20e's confirm-live pass behind the Alert Scheduler
/// panel's own kebab menu -- a flat list of "<b>sent</b> email to &lt;address&gt; / &lt;alert type&gt;
/// / &lt;timestamp&gt;" rows.
///
/// <para><b>This is the scheduler's correctness mechanism, not a nice-to-have history table.</b>
/// The dispatcher inserts the row (status <see cref="AlertSendStatus.Pending"/>) and commits it
/// *before* calling IEmailSender, under a unique index on
/// (AlertDefinitionId, OccurrenceDate, Recipient). Three properties fall out of that one decision:
/// <list type="bullet">
/// <item>an already-sent occurrence is never sent twice, including across a process restart, because
/// the row survives the restart;</item>
/// <item>two app instances ticking simultaneously cannot both send, because the second insert
/// violates the unique index and that instance skips the recipient;</item>
/// <item>a process that dies between the insert and SMTP leaves a Pending row that is never retried
/// -- deliberate <b>at-most-once</b> delivery, chosen because a duplicate daily summary to a real
/// customer is worse than a missing one, and a missing one is visible right here.</item>
/// </list></para>
///
/// <para>OccurrenceDate is the <b>tenant-local</b> (Nepal, UTC+05:45) calendar date the alert was
/// scheduled for, not a UTC date -- the whole point of the key is "this alert's slot for this
/// business day", and near midnight local those two dates differ.</para>
/// </summary>
public sealed class AlertSendLog
{
    public Guid Id { get; private set; }
    public Guid OrganizationId { get; private set; }
    public Guid AlertDefinitionId { get; private set; }

    /// <summary>Denormalised from the definition so the log survives the definition being edited or
    /// deleted and still reads correctly -- the reference product's own Email Logs panel shows the
    /// alert type per row, and an alert retyped from CRM Report to Daily Transaction Summary must
    /// not silently rewrite last week's history.</summary>
    public AlertType AlertType { get; private set; }

    public DateOnly OccurrenceDate { get; private set; }
    public string Recipient { get; private set; } = null!;
    public string Subject { get; private set; } = null!;
    public AlertSendStatus Status { get; private set; }
    public string? FailureReason { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? CompletedAt { get; private set; }

    private AlertSendLog()
    {
    }

    /// <summary>Claims the (definition, occurrence, recipient) slot. Commit this before sending.</summary>
    public static AlertSendLog Claim(
        Guid organizationId,
        Guid alertDefinitionId,
        AlertType alertType,
        DateOnly occurrenceDate,
        string recipient,
        string subject,
        DateTimeOffset now)
    {
        return new AlertSendLog
        {
            Id = Guid.NewGuid(),
            OrganizationId = organizationId,
            AlertDefinitionId = alertDefinitionId,
            AlertType = alertType,
            OccurrenceDate = occurrenceDate,
            Recipient = recipient,
            Subject = subject,
            Status = AlertSendStatus.Pending,
            CreatedAt = now,
        };
    }

    public void MarkSent(DateTimeOffset now)
    {
        Status = AlertSendStatus.Sent;
        FailureReason = null;
        CompletedAt = now;
    }

    /// <summary>Reason is truncated rather than rejected -- an SMTP exception message is arbitrary
    /// third-party text and losing the tail of it must never fail the SaveChanges that records the
    /// failure in the first place.</summary>
    public void MarkFailed(DateTimeOffset now, string reason)
    {
        const int MaxReasonLength = 1000;

        Status = AlertSendStatus.Failed;
        FailureReason = reason.Length > MaxReasonLength ? reason[..MaxReasonLength] : reason;
        CompletedAt = now;
    }
}
