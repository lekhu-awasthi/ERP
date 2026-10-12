using ErpApp.Application.Accounting.Posting;
using ErpApp.Application.Common.Exceptions;
using ErpApp.Application.Common.Persistence;
using ErpApp.Domain.Common;
using ErpApp.Domain.Payments;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.Payments.Queries.GetPayment;

public sealed class GetPaymentQueryHandler(IAppDbContext db) : IRequestHandler<GetPaymentQuery, PaymentDetailDto>
{
    public async Task<PaymentDetailDto> Handle(GetPaymentQuery request, CancellationToken cancellationToken)
    {
        var payment = await db.Payments
            .SingleOrDefaultAsync(x => x.Id == request.Id && x.OrganizationId == request.OrganizationId, cancellationToken)
            ?? throw new NotFoundException("Payment not found.");

        var allocations = await db.PaymentAllocations
            .Where(x => x.SourceType == DocumentType.Payment && x.SourceId == payment.Id)
            .ToListAsync(cancellationToken);

        var invoiceIds = allocations.Where(x => x.TargetDocumentType == DocumentType.Invoice)
            .Select(x => x.TargetDocumentId).ToList();
        var billIds = allocations.Where(x => x.TargetDocumentType == DocumentType.PurchaseBill)
            .Select(x => x.TargetDocumentId).ToList();
        var codes = new Dictionary<Guid, string?>();
        if (invoiceIds.Count > 0)
        {
            foreach (var row in await db.Invoices
                         .Where(x => x.OrganizationId == request.OrganizationId && invoiceIds.Contains(x.Id))
                         .Select(x => new { x.Id, x.Code })
                         .ToListAsync(cancellationToken))
            {
                codes[row.Id] = row.Code;
            }
        }

        if (billIds.Count > 0)
        {
            foreach (var row in await db.PurchaseBills
                         .Where(x => x.OrganizationId == request.OrganizationId && billIds.Contains(x.Id))
                         .Select(x => new { x.Id, x.Code })
                         .ToListAsync(cancellationToken))
            {
                codes[row.Id] = row.Code;
            }
        }

        IReadOnlyList<PostedGlLineDto>? glLines = null;

        if (payment.Status == PaymentStatus.Approved)
        {
            // Phase 36 -- every entry this payment posted, not "the" entry: allocating further
            // against it posts a realised forex leg as a second entry, and a
            // `SingleOrDefaultAsync` throws on two rows just as `SingleAsync` does.
            var glEntries = await SourceDocumentGlEntries.LoadAsync(
                db, DocumentType.Payment, payment.Id, cancellationToken);

            glLines = glEntries.Count == 0
                ? null
                : glEntries.SelectMany(e => e.Lines)
                    .Select(x => new PostedGlLineDto(x.Id, x.AccountId, x.Debit, x.Credit)).ToList();
        }

        return new PaymentDetailDto(
            payment.Id,
            payment.OrganizationId,
            payment.ContactId,
            payment.Direction,
            payment.Code,
            payment.Date,
            payment.PaymentModeId,
            payment.AccountId,
            payment.Amount,
            payment.Reference,
            payment.Status,
            payment.ApprovedByUserId,
            payment.ApprovedAt,
            payment.CreatedAt,
            allocations.Select(x => new PaymentAllocationDto(
                x.Id, x.TargetDocumentType, x.TargetDocumentId, x.Amount, codes.GetValueOrDefault(x.TargetDocumentId)))
                .ToList(),
            glLines,
            payment.CurrencyCode,
            payment.ExchangeRate,
            payment.LocationId);
    }
}
