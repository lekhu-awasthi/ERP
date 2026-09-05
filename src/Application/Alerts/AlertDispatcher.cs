using ErpApp.Application.Common.Email;
using ErpApp.Application.Common.Persistence;
using ErpApp.Application.Common.Sms;
using ErpApp.Domain.Common;
using ErpApp.Domain.Configuration;
using ErpApp.Domain.Crm;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace ErpApp.Application.Alerts;

/// <summary>
/// Decides which alert occurrences are due and sends them. See <see cref="IAlertDispatcher"/> for
/// why this is separate from the hosted service, and docs/phase-20e-status.md for Decisions A-C.
///
/// <para><b>Due rule (Decision C).</b> An occurrence is identified by (definition, tenant-local
/// calendar date). On every tick this looks only at <i>today's</i> local date, and treats a
/// definition as due once the local clock has passed its ScheduleTime. Two consequences, both
/// intended: an alert whose slot was missed because the process was down still fires when the
/// process comes back, <i>the same local day</i>; and a process that was down for three days does
/// not emit a three-day backlog on restart, because yesterday's occurrence is simply never
/// considered again. Catch-up is bounded at one late send per definition per day.</para>
///
/// <para><b>Delivery is at-most-once.</b> The ledger row is committed before SMTP is called (see
/// <see cref="AlertSendLog"/>), so a crash between the two leaves a Pending row that is never
/// retried, and a send that throws is recorded Failed and not retried within the occurrence. A
/// duplicate daily summary to a real customer is worse than a missing one, and a missing one is
/// visible in the Email Logs screen.</para>
///
/// <para><b>This is the one query in the codebase that is deliberately cross-tenant.</b> Every
/// MediatR handler filters by OrganizationId because it acts for one signed-in user in one
/// organization; this is not a handler and has no caller -- it serves every tenant's schedule. Each
/// definition's own OrganizationId is then the only scope its content build ever sees, so no
/// tenant's data can reach another tenant's recipients.</para>
/// </summary>
public sealed class AlertDispatcher(
    IAppDbContext db,
    IEmailSender emailSender,
    ISmsSender smsSender,
    IEnumerable<IAlertContentBuilder> contentBuilders,
    TimeProvider timeProvider,
    ILogger<AlertDispatcher> logger) : IAlertDispatcher
{
    public async Task<int> DispatchDueAsync(CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        var occurrenceDate = NepalTime.LocalDate(now);
        var localTimeOfDay = NepalTime.LocalTimeOfDay(now);

        var dueDefinitions = await db.AlertDefinitions
            .Where(a => a.IsActive
                        && a.Frequency == AlertScheduleFrequency.Daily
                        && a.ScheduleTime <= localTimeOfDay)
            .OrderBy(a => a.CreatedAt)
            .ToListAsync(cancellationToken);

        var sentCount = 0;

        foreach (var definition in dueDefinitions)
        {
            cancellationToken.ThrowIfCancellationRequested();
            sentCount += await DispatchDefinitionAsync(definition, occurrenceDate, cancellationToken);
        }

        return sentCount;
    }

    private async Task<int> DispatchDefinitionAsync(
        AlertDefinition definition, DateOnly occurrenceDate, CancellationToken cancellationToken)
    {
        var recipients = definition.RecipientAddresses;
        if (recipients.Count == 0)
        {
            // Validation makes this unreachable for anything created through the API; a definition
            // with no recipients is a no-op rather than an error, since there is nobody to tell.
            return 0;
        }

        var alreadyClaimed = await db.AlertSendLogs
            .Where(l => l.AlertDefinitionId == definition.Id && l.OccurrenceDate == occurrenceDate)
            .Select(l => l.Recipient)
            .ToListAsync(cancellationToken);

        var pending = recipients
            .Where(r => !alreadyClaimed.Contains(r, StringComparer.OrdinalIgnoreCase))
            .ToList();

        if (pending.Count == 0)
        {
            return 0;
        }

        AlertContent content;
        try
        {
            // Built once per definition, not once per recipient -- every recipient of one
            // occurrence gets identical content by definition, and the build is the expensive part.
            content = await BuildContentAsync(definition, occurrenceDate, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // A build failure is the whole occurrence's failure, so it is recorded against every
            // pending recipient rather than swallowed -- otherwise a permanently broken builder
            // would retry silently on every tick, forever, with nothing visible in Email Logs.
            logger.LogError(ex, "Failed to build content for alert {AlertDefinitionId} ({AlertType}).",
                definition.Id, definition.AlertType);

            foreach (var recipient in pending)
            {
                await ClaimAndRecordFailureAsync(definition, occurrenceDate, recipient, ex.Message, cancellationToken);
            }

            return 0;
        }

        // An SMS alert spends real money, so it is checked before a single message goes out --
        // exactly as SendSmsCommandHandler does for an interactive send, and for the same reason:
        // failing halfway through a batch leaves some recipients told and some not, with the ledger
        // already partly debited. An unaffordable occurrence is recorded as a failure against every
        // recipient rather than thrown, because an exception inside a timer tick is invisible and
        // this is precisely the kind of problem an admin needs to see in the send log.
        if (definition.Medium == AlertMedium.Sms)
        {
            var balance = await db.SmsCreditLedgerEntries
                .Where(x => x.OrganizationId == definition.OrganizationId)
                .SumAsync(x => x.ChangeAmount, cancellationToken);

            if (balance < pending.Count)
            {
                var reason = $"Insufficient SMS credit: {pending.Count} needed, {balance} available.";
                logger.LogWarning(
                    "Alert {AlertDefinitionId} could not send: {Reason}", definition.Id, reason);

                foreach (var recipient in pending)
                {
                    await ClaimAndRecordFailureAsync(
                        definition, occurrenceDate, recipient, reason, cancellationToken);
                }

                return 0;
            }
        }

        var sentCount = 0;

        foreach (var recipient in pending)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var claim = await TryClaimAsync(definition, occurrenceDate, recipient, content.Subject, cancellationToken);
            if (claim is null)
            {
                // Another instance won the race for this occurrence -- its unique index rejected
                // our insert. Skipping is the correct outcome, not an error.
                continue;
            }

            try
            {
                await DeliverAsync(definition, content, recipient, cancellationToken);
                claim.MarkSent(timeProvider.GetUtcNow());
                sentCount++;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Failed to send alert {AlertDefinitionId} to a recipient.", definition.Id);
                claim.MarkFailed(timeProvider.GetUtcNow(), ex.Message);
            }

            await db.SaveChangesAsync(cancellationToken);
        }

        return sentCount;
    }

    /// <summary>
    /// The one place the medium matters. Phase 20e predicted an <see cref="AlertMedium.Sms"/> member
    /// would be "one enum member and a branch"; this is the branch, and the two things it does
    /// beyond branching -- dropping the subject, and debiting a credit -- are why that estimate was
    /// short. See <see cref="AlertMedium"/>.
    ///
    /// <para>The credit debit is written here, per recipient, rather than once per occurrence,
    /// because the ledger must not be debited for a message that threw on its way out. Affordability
    /// was already checked for the whole batch before the loop started, so this cannot overdraw.</para>
    /// </summary>
    private async Task DeliverAsync(
        AlertDefinition definition, AlertContent content, string recipient, CancellationToken cancellationToken)
    {
        if (definition.Medium == AlertMedium.Email)
        {
            await emailSender.SendAsync(recipient, content.Subject, content.Body, cancellationToken);
            return;
        }

        // An SMS has no subject line; the body alone is the message. Prefixing it with the subject
        // would spend the recipient's first line on the alert's internal name.
        await smsSender.SendAsync(recipient, content.Body, cancellationToken);

        db.SmsCreditLedgerEntries.Add(SmsCreditLedgerEntry.CreateSendDebit(
            definition.OrganizationId,
            creditsUsed: 1,
            batchId: definition.Id,
            createdByUserId: definition.CreatedByUserId));
    }

    private Task<AlertContent> BuildContentAsync(
        AlertDefinition definition, DateOnly occurrenceDate, CancellationToken cancellationToken)
    {
        var builder = contentBuilders.SingleOrDefault(b => b.AlertType == definition.AlertType)
            ?? throw new InvalidOperationException(
                $"No IAlertContentBuilder is registered for alert type {definition.AlertType}.");

        return builder.BuildAsync(definition.OrganizationId, occurrenceDate, cancellationToken);
    }

    /// <summary>Inserts and commits the Pending ledger row. Returns null when the insert lost a race
    /// against another instance (the unique index on (AlertDefinitionId, OccurrenceDate, Recipient)
    /// is what detects that), in which case the entry is detached so the shared change tracker is
    /// not left holding a row that will fail every subsequent SaveChanges.</summary>
    private async Task<AlertSendLog?> TryClaimAsync(
        AlertDefinition definition,
        DateOnly occurrenceDate,
        string recipient,
        string subject,
        CancellationToken cancellationToken)
    {
        var claim = AlertSendLog.Claim(
            definition.OrganizationId,
            definition.Id,
            definition.AlertType,
            definition.Medium,
            occurrenceDate,
            recipient,
            subject,
            timeProvider.GetUtcNow());

        db.AlertSendLogs.Add(claim);

        try
        {
            await db.SaveChangesAsync(cancellationToken);
            return claim;
        }
        catch (DbUpdateException ex)
        {
            logger.LogInformation(ex,
                "Alert {AlertDefinitionId} occurrence {OccurrenceDate} was already claimed elsewhere; skipping.",
                definition.Id, occurrenceDate);

            db.AlertSendLogs.Entry(claim).State = EntityState.Detached;
            return null;
        }
    }

    private async Task ClaimAndRecordFailureAsync(
        AlertDefinition definition,
        DateOnly occurrenceDate,
        string recipient,
        string reason,
        CancellationToken cancellationToken)
    {
        var claim = await TryClaimAsync(
            definition, occurrenceDate, recipient, $"{definition.Name} ({definition.AlertType})", cancellationToken);

        if (claim is null)
        {
            return;
        }

        claim.MarkFailed(timeProvider.GetUtcNow(), reason);
        await db.SaveChangesAsync(cancellationToken);
    }
}
