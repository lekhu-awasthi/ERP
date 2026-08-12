using ErpApp.Application.Accounting;
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
        await PaymentValidation.EnsureContactExistsAsync(db, request.OrganizationId, request.ContactId, cancellationToken);
        await AccountingValidation.EnsureAccountsExistAsync(db, request.OrganizationId, [request.AccountId], cancellationToken);
        await PaymentValidation.EnsurePaymentModeExistsAsync(db, request.OrganizationId, request.PaymentModeId, cancellationToken);
        await PaymentValidation.EnsureAllocationTargetsExistAsync(db, request.OrganizationId, request.Allocations, cancellationToken);

        var payment = Payment.Create(
            request.OrganizationId, request.ContactId, PaymentDirection.Received, request.Date, request.PaymentModeId,
            request.AccountId, request.Amount, request.Reference);
        foreach (var allocation in request.Allocations)
        {
            payment.AddAllocation(allocation.TargetDocumentType, allocation.TargetDocumentId, allocation.Amount);
        }

        db.Payments.Add(payment);
        await db.SaveChangesAsync(cancellationToken);

        return new CreatePaymentResult(payment.Id, payment.Code, payment.Status);
    }
}
