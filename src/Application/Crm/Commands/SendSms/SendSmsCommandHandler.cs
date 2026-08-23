using ErpApp.Application.Common.Exceptions;
using ErpApp.Application.Common.Persistence;
using ErpApp.Application.Common.Security;
using ErpApp.Application.Common.Sms;
using ErpApp.Application.Contacts.Queries.ContactStatement;
using ErpApp.Domain.Contacts;
using ErpApp.Domain.Crm;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.Crm.Commands.SendSms;

/// <summary>
/// Atomicity (docs/phase-18-status.md exit criteria #5) is achieved by construction, not by an
/// explicit database transaction: every recipient's ISmsSender.SendAsync call happens first,
/// purely in-memory/against the external channel, before a single db.SmsLogs.AddRange +
/// db.SmsCreditLedgerEntries.Add + one SaveChangesAsync call at the very end. If any recipient's
/// send throws partway through the loop, the method exits before anything has been added to the
/// DbContext at all -- zero partial SmsLog rows, unchanged ledger balance, no rollback machinery
/// needed. Credit sufficiency is checked before the loop even starts, so "insufficient credit"
/// fails the same way (nothing written) rather than partway through a real send.
/// </summary>
public sealed class SendSmsCommandHandler(IAppDbContext db, ISmsSender smsSender, ICurrentUserService currentUser)
    : IRequestHandler<SendSmsCommand, SendSmsResult>
{
    public async Task<SendSmsResult> Handle(SendSmsCommand request, CancellationToken cancellationToken)
    {
        var recipients = await ResolveRecipientsAsync(db, request, cancellationToken);
        if (recipients.Count == 0)
        {
            throw new ConflictException("No recipients with a phone number matched the selected audience.");
        }

        // Flat 1-credit-per-recipient cost model -- Tigg's own real gateway prices by character-
        // count segments, but there's no real gateway here to bill against, and a flat model keeps
        // the atomic-decrement behavior simple and deterministic to test. See SmsCreditLedgerEntry's
        // own doc comment.
        var creditsNeeded = recipients.Count;
        var currentBalance = await db.SmsCreditLedgerEntries
            .Where(x => x.OrganizationId == request.OrganizationId)
            .SumAsync(x => x.ChangeAmount, cancellationToken);

        if (currentBalance < creditsNeeded)
        {
            throw new ConflictException(
                $"Insufficient SMS credit: {creditsNeeded} needed, {currentBalance} available.");
        }

        var batchId = Guid.NewGuid();
        var logs = new List<SmsLog>();

        foreach (var contact in recipients)
        {
            var resolvedContent = await ResolveMergeFieldsAsync(db, contact, request.Content, cancellationToken);

            await smsSender.SendAsync(contact.Phone!, resolvedContent, cancellationToken);

            logs.Add(SmsLog.Create(
                request.OrganizationId, batchId, contact.Id, request.TemplateId, request.Title, resolvedContent,
                contact.Phone!, creditsUsed: 1, currentUser.UserId));
        }

        db.SmsLogs.AddRange(logs);
        db.SmsCreditLedgerEntries.Add(
            SmsCreditLedgerEntry.CreateSendDebit(request.OrganizationId, creditsNeeded, batchId, currentUser.UserId));

        await db.SaveChangesAsync(cancellationToken);

        return new SendSmsResult(batchId, recipients.Count, creditsNeeded, currentBalance - creditsNeeded);
    }

    /// <summary>Only active Contacts with a phone number on file are ever eligible -- a Contact
    /// missing a phone number is silently excluded (not an error), matching the live Tigg screen's
    /// own "Invalid Contact(s)" counter, which likewise drops unreachable rows from the sendable
    /// set rather than failing the whole batch.</summary>
    private static readonly IReadOnlyList<Guid> EmptyContactIds = [];

    private static async Task<List<Contact>> ResolveRecipientsAsync(
        IAppDbContext db, SendSmsCommand request, CancellationToken cancellationToken)
    {
        var query = db.Contacts.Where(x =>
            x.OrganizationId == request.OrganizationId && x.IsActive && x.Phone != null && x.Phone != "");

        query = request.AudienceMode switch
        {
            SmsAudienceMode.All => query,
            SmsAudienceMode.ContactGroup => query.Where(x => x.GroupId == request.ContactGroupId),
            SmsAudienceMode.Custom => query.Where(x => (request.ContactIds ?? EmptyContactIds).Contains(x.Id)),
            _ => throw new ArgumentOutOfRangeException(nameof(request), request.AudienceMode, null),
        };

        return await query.ToListAsync(cancellationToken);
    }

    /// <summary>Merge syntax confirmed live against the Tigg reference product's own Templates
    /// screen: $[name]$, $[balance]$, $[balance_date]$ (SmsTemplate's own doc comment). Unlike
    /// Tigg's own limitation ("merge tags only work when sending from the contact detail page"),
    /// resolved for every recipient on every send here, including bulk sends -- see this file's own
    /// class-level doc comment.</summary>
    private static async Task<string> ResolveMergeFieldsAsync(
        IAppDbContext db, Contact contact, string content, CancellationToken cancellationToken)
    {
        var resolved = content.Replace("$[name]$", contact.Name);

        if (resolved.Contains("$[balance]$") || resolved.Contains("$[balance_date]$"))
        {
            var asOfDate = DateOnly.FromDateTime(DateTime.UtcNow);
            var events = await ContactLedgerReader.LoadEventsAsync(
                db, contact.OrganizationId, contact.Type, contact.Id, asOfDate, cancellationToken);
            var balance = contact.OpeningBalance + events.Sum(x => x.SignedAmount);

            resolved = resolved
                .Replace("$[balance]$", balance.ToString("N2"))
                .Replace("$[balance_date]$", asOfDate.ToString("yyyy-MM-dd"));
        }

        return resolved;
    }
}
