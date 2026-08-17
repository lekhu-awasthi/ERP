using ErpApp.Application.Common.Exceptions;
using ErpApp.Application.Common.Persistence;
using ErpApp.Domain.Contacts;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.Contacts.Queries.ContactStatement;

/// <summary>
/// Every Approved document for this Contact contributes its own signed delta directly -- unlike
/// ContactAgeingSummaryQuery, a flat ledger doesn't need to attribute a CreditNote/DebitNote/Payment
/// to a *specific* bill, so a standalone (unlinked) reversal is included here exactly like a linked
/// one. This is the one place Ageing and Statement can legitimately disagree for a Contact carrying a
/// standalone reversal -- see ContactAgeingSummaryQueryHandler's own doc comment.
///
/// Event loading lives in ContactLedgerReader (extracted in Phase 10 once ContactOverviewQueryHandler
/// became a second caller needing the exact same signed deltas) -- see that file's own doc comment.
/// </summary>
public sealed class ContactStatementQueryHandler(IAppDbContext db) : IRequestHandler<ContactStatementQuery, ContactStatementDto>
{
    public async Task<ContactStatementDto> Handle(ContactStatementQuery request, CancellationToken cancellationToken)
    {
        var contact = await db.Contacts.SingleOrDefaultAsync(
                x => x.Id == request.ContactId && x.OrganizationId == request.OrganizationId && x.Type == request.ContactType,
                cancellationToken)
            ?? throw new NotFoundException($"{request.ContactType} not found.");

        var events = await ContactLedgerReader.LoadEventsAsync(
            db, request.OrganizationId, request.ContactType, request.ContactId, request.ToDate, cancellationToken);

        var openingBalance = contact.OpeningBalance + events
            .Where(x => x.Date < request.FromDate)
            .Sum(x => x.SignedAmount);

        var running = openingBalance;
        var rows = new List<ContactStatementRowDto>();

        foreach (var e in events.Where(x => x.Date >= request.FromDate && x.Date <= request.ToDate)
                     .OrderBy(x => x.Date).ThenBy(x => x.Code))
        {
            running += e.SignedAmount;

            var debit = request.ContactType == ContactType.Customer
                ? Math.Max(e.SignedAmount, 0)
                : Math.Max(-e.SignedAmount, 0);
            var credit = request.ContactType == ContactType.Customer
                ? Math.Max(-e.SignedAmount, 0)
                : Math.Max(e.SignedAmount, 0);

            rows.Add(new ContactStatementRowDto(
                e.Date, e.DocumentType, e.Code, e.Reference, debit, credit, Math.Abs(running),
                ContactLedgerReader.BalanceType(request.ContactType, running)));
        }

        return new ContactStatementDto(
            contact.Id, contact.Code, contact.Name, contact.Type, request.FromDate, request.ToDate,
            Math.Abs(openingBalance), ContactLedgerReader.BalanceType(request.ContactType, openingBalance),
            rows, Math.Abs(running), ContactLedgerReader.BalanceType(request.ContactType, running));
    }
}
