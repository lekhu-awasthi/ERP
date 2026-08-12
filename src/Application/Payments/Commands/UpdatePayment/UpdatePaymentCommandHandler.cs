using ErpApp.Application.Accounting;
using ErpApp.Application.Common.Exceptions;
using ErpApp.Application.Common.Persistence;
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
            .Include(x => x.Allocations)
            .SingleOrDefaultAsync(x => x.Id == request.Id && x.OrganizationId == request.OrganizationId, cancellationToken)
            ?? throw new NotFoundException("Payment not found.");

        if (payment.Status != PaymentStatus.Draft)
        {
            throw new ConflictException("Only a Draft payment can be edited.");
        }

        await PaymentValidation.EnsureContactExistsAsync(db, request.OrganizationId, request.ContactId, payment.Direction, cancellationToken);
        await AccountingValidation.EnsureAccountsExistAsync(db, request.OrganizationId, [request.AccountId], cancellationToken);
        await PaymentValidation.EnsurePaymentModeExistsAsync(db, request.OrganizationId, request.PaymentModeId, cancellationToken);
        await PaymentValidation.EnsureAllocationTargetsExistAsync(db, request.OrganizationId, request.Allocations, cancellationToken);

        var oldAllocations = payment.Allocations.ToList();

        payment.UpdateHeader(request.ContactId, request.Date, request.PaymentModeId, request.AccountId, request.Amount, request.Reference);
        payment.ClearAllocations();
        foreach (var allocation in request.Allocations)
        {
            payment.AddAllocation(allocation.TargetDocumentType, allocation.TargetDocumentId, allocation.Amount);
        }

        db.PaymentAllocations.RemoveRange(oldAllocations);
        db.PaymentAllocations.AddRange(payment.Allocations);

        await db.SaveChangesAsync(cancellationToken);

        return new UpdatePaymentResult(payment.Id, payment.Code, payment.Status);
    }
}
