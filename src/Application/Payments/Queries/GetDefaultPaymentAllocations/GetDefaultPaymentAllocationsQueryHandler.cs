using ErpApp.Application.Common.Persistence;
using ErpApp.Domain.Common;
using ErpApp.Domain.Payments;
using ErpApp.Domain.Sales;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.Payments.Queries.GetDefaultPaymentAllocations;

public sealed class GetDefaultPaymentAllocationsQueryHandler(IAppDbContext db)
    : IRequestHandler<GetDefaultPaymentAllocationsQuery, IReadOnlyList<PaymentAllocationInput>>
{
    public async Task<IReadOnlyList<PaymentAllocationInput>> Handle(
        GetDefaultPaymentAllocationsQuery request, CancellationToken cancellationToken)
    {
        var outstandingInvoices = await db.Invoices
            .Where(x => x.OrganizationId == request.OrganizationId && x.ContactId == request.ContactId && x.Status == InvoiceStatus.Approved)
            .Include(x => x.Lines)
            .OrderBy(x => x.Date)
            .ToListAsync(cancellationToken);

        if (outstandingInvoices.Count == 0)
        {
            return [];
        }

        var invoiceIds = outstandingInvoices.Select(x => x.Id).ToList();

        // Only allocations belonging to an already-Approved payment reduce "outstanding" -- a
        // Draft payment's tentative allocation hasn't been posted yet.
        var allocatedByInvoice = await (
                from a in db.PaymentAllocations
                join p in db.Payments on a.PaymentId equals p.Id
                where a.TargetDocumentType == DocumentType.Invoice
                      && invoiceIds.Contains(a.TargetDocumentId)
                      && p.Status == PaymentStatus.Approved
                group a by a.TargetDocumentId into g
                select new { InvoiceId = g.Key, Allocated = g.Sum(a => a.Amount) })
            .ToDictionaryAsync(x => x.InvoiceId, x => x.Allocated, cancellationToken);

        var suggestions = new List<PaymentAllocationInput>();
        var remaining = request.Amount;

        foreach (var invoice in outstandingInvoices)
        {
            if (remaining <= 0)
            {
                break;
            }

            var allocated = allocatedByInvoice.GetValueOrDefault(invoice.Id, 0m);
            var outstanding = invoice.GrandTotal - allocated;
            if (outstanding <= 0)
            {
                continue;
            }

            var take = Math.Min(remaining, outstanding);
            suggestions.Add(new PaymentAllocationInput(DocumentType.Invoice, invoice.Id, take));
            remaining -= take;
        }

        return suggestions;
    }
}
