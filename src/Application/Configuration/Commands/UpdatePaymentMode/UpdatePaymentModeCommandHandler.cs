using ErpApp.Application.Common.Exceptions;
using ErpApp.Application.Common.Persistence;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.Configuration.Commands.UpdatePaymentMode;

public sealed class UpdatePaymentModeCommandHandler(IAppDbContext db)
    : IRequestHandler<UpdatePaymentModeCommand, UpdatePaymentModeResult>
{
    public async Task<UpdatePaymentModeResult> Handle(UpdatePaymentModeCommand request, CancellationToken cancellationToken)
    {
        var paymentMode = await db.PaymentModes.SingleOrDefaultAsync(
            x => x.Id == request.Id && x.OrganizationId == request.OrganizationId, cancellationToken)
            ?? throw new NotFoundException("Payment mode not found.");

        var nameTaken = await db.PaymentModes.AnyAsync(
            x => x.OrganizationId == request.OrganizationId && x.Id != request.Id && x.Name == request.Name,
            cancellationToken);

        if (nameTaken)
        {
            throw new ConflictException($"A payment mode named '{request.Name}' already exists.");
        }

        await PaymentModeAccountRule.EnsureCashOrBankAsync(db, request.OrganizationId, request.AccountId, cancellationToken);

        // Phase 60 -- guard the edit as well as the link (phase 45: guard the add and the edit).
        // A linked mode must keep an account, and must stay active, or a till offers a tab that
        // cannot post. Unlinking first is the way out, and says what it does.
        if (request.AccountId is null || !request.IsActive)
        {
            var linked = await db.PosLocationPaymentModes.AnyAsync(
                x => x.OrganizationId == request.OrganizationId && x.PaymentModeId == request.Id, cancellationToken);

            if (linked)
            {
                throw new ConflictException(
                    $"'{paymentMode.Name}' is offered at a point-of-sale location, so it must stay active and keep "
                    + "its payment account. Unlink it from Configurations > Point of Sale first.");
            }
        }

        paymentMode.Update(request.Name, request.IsActive, request.RequiresChequeDetails, request.Kind, request.AccountId);
        await db.SaveChangesAsync(cancellationToken);

        return new UpdatePaymentModeResult(
            paymentMode.Id, paymentMode.Name, paymentMode.IsActive, paymentMode.RequiresChequeDetails,
            paymentMode.Kind, paymentMode.AccountId);
    }
}
