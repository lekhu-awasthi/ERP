using ErpApp.Application.Common.Persistence;
using ErpApp.Application.Common.Security;
using ErpApp.Application.Pos.Sessions;
using ErpApp.Domain.Sales;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.Pos.Queries.ListPosSessionSales;

public sealed class ListPosSessionSalesQueryHandler(IAppDbContext db, ICurrentUserService currentUser)
    : IRequestHandler<ListPosSessionSalesQuery, IReadOnlyList<PosSessionSaleDto>>
{
    public async Task<IReadOnlyList<PosSessionSaleDto>> Handle(
        ListPosSessionSalesQuery request, CancellationToken cancellationToken)
    {
        var session = await PosSessionAccess.LoadReadableAsync(
            db, request.OrganizationId, request.SessionId, currentUser.UserId, cancellationToken);

        // Voided sales stay on the list, marked: a cashier who voided one should see it went, and
        // the session's figures already leave it out (PosSalesReader).
        var invoices = await db.Invoices
            .AsNoTracking()
            .Include(x => x.Lines)
            .Include(x => x.Tenders)
            .Where(x => x.OrganizationId == request.OrganizationId && x.PosSessionId == session.Id
                && x.Channel == SalesChannel.Pos && x.Status != InvoiceStatus.Draft)
            .OrderByDescending(x => x.ApprovedAt)
            .ToListAsync(cancellationToken);

        var contactIds = invoices.Select(x => x.ContactId).Distinct().ToList();
        var contacts = await db.Contacts
            .Where(x => x.OrganizationId == request.OrganizationId && contactIds.Contains(x.Id))
            .ToDictionaryAsync(x => x.Id, x => new { x.Name, x.IsWalkInCustomer }, cancellationToken);

        var invoiceIds = invoices.Select(x => x.Id).ToList();
        var printCounts = await db.InvoicePrints
            .Where(x => x.OrganizationId == request.OrganizationId && invoiceIds.Contains(x.InvoiceId))
            .GroupBy(x => x.InvoiceId)
            .Select(g => new { InvoiceId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.InvoiceId, x => x.Count, cancellationToken);

        return invoices
            .Select(x =>
            {
                var contact = contacts.GetValueOrDefault(x.ContactId);
                return new PosSessionSaleDto(
                    x.Id,
                    x.Code,
                    x.Status,
                    x.ApprovedAt,
                    contact?.Name ?? "",
                    contact?.IsWalkInCustomer ?? false,
                    x.GrandTotal,
                    x.TenderedAmount,
                    x.ChangeAmount,
                    x.CreditAmount,
                    x.IsAbbreviatedTaxInvoice,
                    printCounts.GetValueOrDefault(x.Id));
            })
            .ToList();
    }
}
