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
            .Include(x => x.Allocations)
            .SingleOrDefaultAsync(x => x.Id == request.Id && x.OrganizationId == request.OrganizationId, cancellationToken)
            ?? throw new NotFoundException("Payment not found.");

        IReadOnlyList<PostedGlLineDto>? glLines = null;

        if (payment.Status == PaymentStatus.Approved)
        {
            var glEntry = await db.GlJournalEntries
                .Include(x => x.Lines)
                .SingleOrDefaultAsync(
                    x => x.SourceDocumentType == DocumentType.Payment && x.SourceDocumentId == payment.Id, cancellationToken);

            glLines = glEntry?.Lines.Select(x => new PostedGlLineDto(x.Id, x.AccountId, x.Debit, x.Credit)).ToList();
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
            payment.Allocations.Select(x => new PaymentAllocationDto(x.Id, x.TargetDocumentType, x.TargetDocumentId, x.Amount)).ToList(),
            glLines);
    }
}
