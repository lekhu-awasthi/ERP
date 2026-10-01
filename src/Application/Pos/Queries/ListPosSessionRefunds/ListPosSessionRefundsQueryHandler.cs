using ErpApp.Application.Common.Persistence;
using ErpApp.Application.Common.Security;
using ErpApp.Application.Pos.Sessions;
using ErpApp.Domain.Common;
using ErpApp.Domain.Sales;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.Pos.Queries.ListPosSessionRefunds;

public sealed class ListPosSessionRefundsQueryHandler(IAppDbContext db, ICurrentUserService currentUser)
    : IRequestHandler<ListPosSessionRefundsQuery, IReadOnlyList<PosSessionRefundDto>>
{
    public async Task<IReadOnlyList<PosSessionRefundDto>> Handle(
        ListPosSessionRefundsQuery request, CancellationToken cancellationToken)
    {
        var session = await PosSessionAccess.LoadReadableAsync(
            db, request.OrganizationId, request.SessionId, currentUser.UserId, cancellationToken);

        // Voided refunds stay on the list, marked, as voided sales do on the sales list.
        var notes = await db.CreditNotes
            .AsNoTracking()
            .Include(x => x.Lines)
            .Include(x => x.Payouts)
            .Where(x => x.OrganizationId == request.OrganizationId && x.PosSessionId == session.Id
                && x.Channel == SalesChannel.Pos && x.Status != CreditNoteStatus.Draft)
            .OrderByDescending(x => x.ApprovedAt)
            .ToListAsync(cancellationToken);

        var contactIds = notes.Select(x => x.ContactId).Distinct().ToList();
        var contacts = await db.Contacts
            .Where(x => x.OrganizationId == request.OrganizationId && contactIds.Contains(x.Id))
            .ToDictionaryAsync(x => x.Id, x => new { x.Name, x.IsWalkInCustomer }, cancellationToken);

        var invoiceIds = notes
            .Where(x => x.ReferrerType == DocumentType.Invoice && x.ReferrerId is not null)
            .Select(x => x.ReferrerId!.Value)
            .Distinct()
            .ToList();
        var invoiceCodes = await db.Invoices
            .Where(x => x.OrganizationId == request.OrganizationId && invoiceIds.Contains(x.Id))
            .ToDictionaryAsync(x => x.Id, x => x.Code, cancellationToken);

        var noteIds = notes.Select(x => x.Id).ToList();
        var printCounts = await db.CreditNotePrints
            .Where(x => x.OrganizationId == request.OrganizationId && noteIds.Contains(x.CreditNoteId))
            .GroupBy(x => x.CreditNoteId)
            .Select(g => new { CreditNoteId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.CreditNoteId, x => x.Count, cancellationToken);

        return notes
            .Select(x =>
            {
                var contact = contacts.GetValueOrDefault(x.ContactId);
                return new PosSessionRefundDto(
                    x.Id,
                    x.Code,
                    x.Status,
                    x.ApprovedAt,
                    x.ReferrerId,
                    x.ReferrerId is { } id ? invoiceCodes.GetValueOrDefault(id) : null,
                    contact?.Name ?? "",
                    contact?.IsWalkInCustomer ?? false,
                    x.Reason,
                    x.GrandTotal,
                    x.PaidOutAmount,
                    x.ToAccountAmount,
                    printCounts.GetValueOrDefault(x.Id));
            })
            .ToList();
    }
}
