using ErpApp.Application.Common.Persistence;
using ErpApp.Domain.Common;
using ErpApp.Domain.Payments;
using ErpApp.Domain.Purchasing;
using ErpApp.Domain.Sales;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.Payments.Queries.GetDefaultPaymentAllocations;

/// <summary>
/// FIFO-oldest-first outstanding-suggestion, mirroring ContactAgeingSummaryQueryHandler's own per-bill
/// netting (phase-9-status.md scope decision #7 flagged this handler's prior GrandTotal-only figure as
/// a pre-existing latent gap; fixed here in Phase 11): outstanding = NetAmount(bill) minus every
/// Approved Payment allocation minus every Approved linked reversal (CreditNote for Invoice, DebitNote
/// for PurchaseBill) whose ReferrerId points at it. PurchaseBill's NetAmount is GrandTotal-TdsAmount --
/// the same net-of-TDS figure PurchaseBillPostingRule posts to Accounts Payable (phase-6-status.md) --
/// not the gross bill total. A standalone (unlinked) CreditNote/DebitNote contributes nothing here,
/// same as it contributes nothing to Ageing's per-bill buckets (phase-9-status.md scope decision #9):
/// it has no specific bill to attribute against, so it can't reduce what's suggested for a specific
/// document either.
/// </summary>
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
                ids => LoadCreditNoteReversalsAsync(request.OrganizationId, ids, cancellationToken),
                DocumentType.Invoice, request.Amount, db, cancellationToken)
            : await SuggestAsync(
                db.PurchaseBills
                    .Include(x => x.Lines)
                    .Where(x => x.OrganizationId == request.OrganizationId && x.ContactId == request.ContactId && x.Status == PurchaseBillStatus.Approved),
                x => x.Id, x => x.Date, x => x.GrandTotal - x.TdsAmount,
                ids => LoadDebitNoteReversalsAsync(request.OrganizationId, ids, cancellationToken),
                DocumentType.PurchaseBill, request.Amount, db, cancellationToken);
    }

    private async Task<Dictionary<Guid, decimal>> LoadCreditNoteReversalsAsync(
        Guid organizationId, IReadOnlyList<Guid> invoiceIds, CancellationToken cancellationToken)
    {
        var creditNotes = await db.CreditNotes
            .Where(x => x.OrganizationId == organizationId && x.Status == CreditNoteStatus.Approved
                && x.ReferrerType == DocumentType.Invoice && x.ReferrerId != null && invoiceIds.Contains(x.ReferrerId.Value))
            .Select(x => new { x.Id, ReferrerId = x.ReferrerId!.Value })
            .ToListAsync(cancellationToken);
        var lines = await db.CreditNoteLines
            .Where(x => creditNotes.Select(c => c.Id).Contains(x.CreditNoteId))
            .Select(x => new { x.CreditNoteId, x.Amount, x.VatAmount })
            .ToListAsync(cancellationToken);
        var gross = lines.GroupBy(x => x.CreditNoteId).ToDictionary(g => g.Key, g => g.Sum(x => x.Amount + x.VatAmount));

        var result = new Dictionary<Guid, decimal>();
        foreach (var cn in creditNotes)
        {
            result[cn.ReferrerId] = result.GetValueOrDefault(cn.ReferrerId) + gross.GetValueOrDefault(cn.Id);
        }

        return result;
    }

    private async Task<Dictionary<Guid, decimal>> LoadDebitNoteReversalsAsync(
        Guid organizationId, IReadOnlyList<Guid> purchaseBillIds, CancellationToken cancellationToken)
    {
        var debitNotes = await db.DebitNotes
            .Where(x => x.OrganizationId == organizationId && x.Status == DebitNoteStatus.Approved
                && x.ReferrerType == DocumentType.PurchaseBill && x.ReferrerId != null && purchaseBillIds.Contains(x.ReferrerId.Value))
            .Select(x => new { x.Id, ReferrerId = x.ReferrerId!.Value, x.TdsAmount })
            .ToListAsync(cancellationToken);
        var lines = await db.DebitNoteLines
            .Where(x => debitNotes.Select(d => d.Id).Contains(x.DebitNoteId))
            .Select(x => new { x.DebitNoteId, x.Amount, x.VatAmount })
            .ToListAsync(cancellationToken);
        var gross = lines.GroupBy(x => x.DebitNoteId).ToDictionary(g => g.Key, g => g.Sum(x => x.Amount + x.VatAmount));

        var result = new Dictionary<Guid, decimal>();
        foreach (var dn in debitNotes)
        {
            var net = gross.GetValueOrDefault(dn.Id) - dn.TdsAmount;
            result[dn.ReferrerId] = result.GetValueOrDefault(dn.ReferrerId) + net;
        }

        return result;
    }

    /// <summary>Shared FIFO-oldest-first suggestion logic for either target document type -- only
    /// allocations belonging to an already-Approved payment reduce "outstanding" (a Draft payment's
    /// tentative allocation hasn't been posted yet).</summary>
    private static async Task<IReadOnlyList<PaymentAllocationInput>> SuggestAsync<TDocument>(
        IQueryable<TDocument> outstandingQuery,
        Func<TDocument, Guid> idSelector,
        Func<TDocument, DateOnly> dateSelector,
        Func<TDocument, decimal> netAmountSelector,
        Func<IReadOnlyList<Guid>, Task<Dictionary<Guid, decimal>>> reversalsByDocumentIdAsync,
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

        // Phase 17 decision #2: scoped to Payment-sourced allocations only -- see the matching note
        // in ContactAgeingSummaryQueryHandler.
        var allocatedByDocument = await (
                from a in db.PaymentAllocations
                where a.SourceType == DocumentType.Payment
                join p in db.Payments on a.SourceId equals p.Id
                where a.TargetDocumentType == targetDocumentType
                      && documentIds.Contains(a.TargetDocumentId)
                      && p.Status == PaymentStatus.Approved
                group a by a.TargetDocumentId into g
                select new { DocumentId = g.Key, Allocated = g.Sum(a => a.Amount) })
            .ToDictionaryAsync(x => x.DocumentId, x => x.Allocated, cancellationToken);

        var reversedByDocument = await reversalsByDocumentIdAsync(documentIds);

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
            var reversed = reversedByDocument.GetValueOrDefault(id, 0m);
            var outstanding = netAmountSelector(document) - allocated - reversed;
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
