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
            allocations.Select(x => new PaymentAllocationDto(x.Id, x.TargetDocumentType, x.TargetDocumentId, x.Amount)).ToList(),
            glLines,
            payment.CurrencyCode,
            payment.ExchangeRate,
            payment.LocationId);
    }
}
