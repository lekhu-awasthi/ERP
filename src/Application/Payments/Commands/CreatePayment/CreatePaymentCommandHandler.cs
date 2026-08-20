using ErpApp.Application.Accounting;
using ErpApp.Application.Common.Exceptions;
using ErpApp.Application.Common.Persistence;
using ErpApp.Domain.Contacts;
using ErpApp.Domain.Payments;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.Payments.Commands.CreatePayment;

public sealed class CreatePaymentCommandHandler(IAppDbContext db)
    : IRequestHandler<CreatePaymentCommand, CreatePaymentResult>
{
    public async Task<CreatePaymentResult> Handle(CreatePaymentCommand request, CancellationToken cancellationToken)
    {
        await PaymentValidation.EnsureContactExistsAsync(db, request.OrganizationId, request.ContactId, request.Direction, cancellationToken);
        await AccountingValidation.EnsureAccountsExistAsync(db, request.OrganizationId, [request.AccountId], cancellationToken);
        var paymentMode = await PaymentValidation.EnsurePaymentModeExistsAsync(db, request.OrganizationId, request.PaymentModeId, cancellationToken);
        await PaymentValidation.EnsureAllocationTargetsExistAsync(db, request.OrganizationId, request.Allocations, cancellationToken);

        var payment = Payment.Create(
            request.OrganizationId, request.ContactId, request.Direction, request.Date, request.PaymentModeId,
            request.AccountId, request.Amount, request.Reference);
        foreach (var allocation in request.Allocations)
        {
            payment.AddAllocation(allocation.TargetDocumentType, allocation.TargetDocumentId, allocation.Amount);
        }

        db.Payments.Add(payment);
        db.PaymentAllocations.AddRange(payment.Allocations);

        if (paymentMode is { RequiresChequeDetails: true })
        {
            if (request.ChequeDetails is null)
            {
                throw new ConflictException("This payment mode requires cheque details.");
            }

            var cheque = Cheque.Create(
                request.OrganizationId, payment.Id, request.Direction, request.AccountId,
                request.ChequeDetails.ChequeNo, request.ChequeDetails.ChequeDate, request.ChequeDetails.ReceivedDate,
                request.Amount);
            db.Cheques.Add(cheque);
        }

        await db.SaveChangesAsync(cancellationToken);

        return new CreatePaymentResult(payment.Id, payment.Code, payment.Status);
    }
}
