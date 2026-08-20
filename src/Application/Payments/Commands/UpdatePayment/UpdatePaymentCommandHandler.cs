using ErpApp.Application.Accounting;
using ErpApp.Application.Common.Exceptions;
using ErpApp.Application.Common.Persistence;
using ErpApp.Domain.Common;
using ErpApp.Domain.Payments;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.Payments.Commands.UpdatePayment;

public sealed class UpdatePaymentCommandHandler(IAppDbContext db)
    : IRequestHandler<UpdatePaymentCommand, UpdatePaymentResult>
{
    public async Task<UpdatePaymentResult> Handle(UpdatePaymentCommand request, CancellationToken cancellationToken)
    {
        var payment = await db.Payments
            .SingleOrDefaultAsync(x => x.Id == request.Id && x.OrganizationId == request.OrganizationId, cancellationToken)
            ?? throw new NotFoundException("Payment not found.");

        if (payment.Status != PaymentStatus.Draft)
        {
            throw new ConflictException("Only a Draft payment can be edited.");
        }

        await PaymentValidation.EnsureContactExistsAsync(db, request.OrganizationId, request.ContactId, payment.Direction, cancellationToken);
        await AccountingValidation.EnsureAccountsExistAsync(db, request.OrganizationId, [request.AccountId], cancellationToken);
        var paymentMode = await PaymentValidation.EnsurePaymentModeExistsAsync(db, request.OrganizationId, request.PaymentModeId, cancellationToken);
        await PaymentValidation.EnsureAllocationTargetsExistAsync(db, request.OrganizationId, request.Allocations, cancellationToken);

        var existingAllocations = await db.PaymentAllocations
            .Where(x => x.SourceType == DocumentType.Payment && x.SourceId == payment.Id)
            .ToListAsync(cancellationToken);
        payment.AttachAllocations(existingAllocations);

        var oldAllocations = payment.Allocations.ToList();

        payment.UpdateHeader(request.ContactId, request.Date, request.PaymentModeId, request.AccountId, request.Amount, request.Reference);
        payment.ClearAllocations();
        foreach (var allocation in request.Allocations)
        {
            payment.AddAllocation(allocation.TargetDocumentType, allocation.TargetDocumentId, allocation.Amount);
        }

        db.PaymentAllocations.RemoveRange(oldAllocations);
        db.PaymentAllocations.AddRange(payment.Allocations);

        var existingCheque = await db.Cheques.SingleOrDefaultAsync(x => x.LinkedPaymentId == payment.Id, cancellationToken);

        if (paymentMode is { RequiresChequeDetails: true })
        {
            if (request.ChequeDetails is null)
            {
                throw new ConflictException("This payment mode requires cheque details.");
            }

            if (existingCheque is not null)
            {
                existingCheque.UpdateDetails(
                    request.AccountId, request.ChequeDetails.ChequeNo, request.ChequeDetails.ChequeDate,
                    request.ChequeDetails.ReceivedDate, request.Amount);
            }
            else
            {
                db.Cheques.Add(Cheque.Create(
                    request.OrganizationId, payment.Id, payment.Direction, request.AccountId,
                    request.ChequeDetails.ChequeNo, request.ChequeDetails.ChequeDate, request.ChequeDetails.ReceivedDate,
                    request.Amount));
            }
        }
        else if (existingCheque is not null)
        {
            db.Cheques.Remove(existingCheque);
        }

        await db.SaveChangesAsync(cancellationToken);

        return new UpdatePaymentResult(payment.Id, payment.Code, payment.Status);
    }
}
