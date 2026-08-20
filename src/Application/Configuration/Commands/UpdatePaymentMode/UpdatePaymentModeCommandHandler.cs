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

        paymentMode.Update(request.Name, request.IsActive, request.RequiresChequeDetails);
        await db.SaveChangesAsync(cancellationToken);

        return new UpdatePaymentModeResult(paymentMode.Id, paymentMode.Name, paymentMode.IsActive, paymentMode.RequiresChequeDetails);
    }
}
