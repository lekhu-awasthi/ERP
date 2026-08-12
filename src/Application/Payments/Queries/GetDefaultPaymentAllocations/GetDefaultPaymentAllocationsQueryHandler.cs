using ErpApp.Application.Common.Persistence;
using ErpApp.Domain.Common;
using ErpApp.Domain.Payments;
using ErpApp.Domain.Purchasing;
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
        return request.Direction == PaymentDirection.Received
            ? await SuggestAsync(
                db.Invoices
                    .Include(x => x.Lines)
                    .Where(x => x.OrganizationId == request.OrganizationId && x.ContactId == request.ContactId && x.Status == InvoiceStatus.Approved),
                x => x.Id, x => x.Date, x => x.GrandTotal,
                DocumentType.Invoice, request.Amount, db, cancellationToken)
            : await SuggestAsync(
                db.PurchaseBills
                    .Include(x => x.Lines)
                    .Where(x => x.OrganizationId == request.OrganizationId && x.ContactId == request.ContactId && x.Status == PurchaseBillStatus.Approved),
                x => x.Id, x => x.Date, x => x.GrandTotal,
                DocumentType.PurchaseBill, request.Amount, db, cancellationToken);
    }

    /// <summary>Shared FIFO-oldest-first suggestion logic for either target document type -- only
    /// allocations belonging to an already-Approved payment reduce "outstanding" (a Draft payment's
    /// tentative allocation hasn't been posted yet).</summary>
    private static async Task<IReadOnlyList<PaymentAllocationInput>> SuggestAsync<TDocument>(
        IQueryable<TDocument> outstandingQuery,
        Func<TDocument, Guid> idSelector,
        Func<TDocument, DateOnly> dateSelector,
        Func<TDocument, decimal> grandTotalSelector,
        DocumentType targetDocumentType,
        decimal amount,
        IAppDbContext db,
        CancellationToken cancellationToken)
        where TDocument : class
    {
        var outstandingDocuments = (await outstandingQuery.ToListAsync(cancellationToken))
            .OrderBy(dateSelector)
            .ToList();

        if (outstandingDocuments.Count == 0)
        {
            return [];
        }

        var documentIds = outstandingDocuments.Select(idSelector).ToList();

        var allocatedByDocument = await (
                from a in db.PaymentAllocations
                join p in db.Payments on a.PaymentId equals p.Id
                where a.TargetDocumentType == targetDocumentType
                      && documentIds.Contains(a.TargetDocumentId)
                      && p.Status == PaymentStatus.Approved
                group a by a.TargetDocumentId into g
                select new { DocumentId = g.Key, Allocated = g.Sum(a => a.Amount) })
            .ToDictionaryAsync(x => x.DocumentId, x => x.Allocated, cancellationToken);

        var suggestions = new List<PaymentAllocationInput>();
        var remaining = amount;

        foreach (var document in outstandingDocuments)
        {
            if (remaining <= 0)
            {
                break;
            }

            var id = idSelector(document);
            var allocated = allocatedByDocument.GetValueOrDefault(id, 0m);
            var outstanding = grandTotalSelector(document) - allocated;
            if (outstanding <= 0)
            {
                continue;
            }

            var take = Math.Min(remaining, outstanding);
            suggestions.Add(new PaymentAllocationInput(targetDocumentType, id, take));
            remaining -= take;
        }

        return suggestions;
    }
}
