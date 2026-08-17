using ErpApp.Application.Common.Exceptions;
using ErpApp.Application.Common.Persistence;
using ErpApp.Application.Contacts.Queries.ContactStatement;
using ErpApp.Domain.Contacts;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.Contacts.Queries.ContactOverview;

public sealed class ContactOverviewQueryHandler(IAppDbContext db) : IRequestHandler<ContactOverviewQuery, ContactOverviewDto>
{
    /// <summary>
    /// No confirmed count from the live screen (erp-module-scan.md only names the widget, not a row
    /// count). A fixed transaction count, not a date window (e.g. "last 90 days"), keeps the widget's
    /// size predictable regardless of how active or dormant a given Contact is -- a slow-moving
    /// Contact wouldn't show an empty widget, and a very active one wouldn't flood it. 10 mirrors a
    /// typical "recent activity" feed size and stays well within a detail-page sidebar's real estate.
    /// </summary>
    private const int RecentTransactionsLimit = 10;

    public async Task<ContactOverviewDto> Handle(ContactOverviewQuery request, CancellationToken cancellationToken)
    {
        var contact = await db.Contacts.SingleOrDefaultAsync(
                x => x.Id == request.ContactId && x.OrganizationId == request.OrganizationId, cancellationToken)
            ?? throw new NotFoundException("Contact not found.");

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var events = await ContactLedgerReader.LoadEventsAsync(
            db, request.OrganizationId, contact.Type, contact.Id, today, cancellationToken);

        // Lead has no real AR/AP subledger to be debit- or credit-normal against; defaulting to
        // Customer's polarity is arbitrary but harmless -- see ContactOverviewQuery's doc comment.
        var polarityType = contact.Type == ContactType.Supplier ? ContactType.Supplier : ContactType.Customer;

        var closingBalance = contact.OpeningBalance + events.Sum(x => x.SignedAmount);

        var recentTransactions = events
            .OrderByDescending(x => x.Date).ThenByDescending(x => x.Code)
            .Take(RecentTransactionsLimit)
            .Select(x => new ContactOverviewTransactionDto(
                x.Date, x.DocumentType, x.Code, x.Reference,
                polarityType == ContactType.Customer ? Math.Max(x.SignedAmount, 0) : Math.Max(-x.SignedAmount, 0),
                polarityType == ContactType.Customer ? Math.Max(-x.SignedAmount, 0) : Math.Max(x.SignedAmount, 0)))
            .ToList();

        return new ContactOverviewDto(
            contact.Id, contact.Code, contact.Name, contact.Type,
            Math.Abs(contact.OpeningBalance), ContactLedgerReader.BalanceType(polarityType, contact.OpeningBalance),
            Math.Abs(closingBalance), ContactLedgerReader.BalanceType(polarityType, closingBalance),
            recentTransactions);
    }
}
