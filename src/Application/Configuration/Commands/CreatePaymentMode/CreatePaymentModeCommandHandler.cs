using ErpApp.Application.Common.Exceptions;
using ErpApp.Application.Common.Persistence;
using ErpApp.Domain.Configuration;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.Configuration.Commands.CreatePaymentMode;

public sealed class CreatePaymentModeCommandHandler(IAppDbContext db)
    : IRequestHandler<CreatePaymentModeCommand, CreatePaymentModeResult>
{
    public async Task<CreatePaymentModeResult> Handle(CreatePaymentModeCommand request, CancellationToken cancellationToken)
    {
        var nameExists = await db.PaymentModes.AnyAsync(
            x => x.OrganizationId == request.OrganizationId && x.Name == request.Name, cancellationToken);

        if (nameExists)
        {
            throw new ConflictException($"A payment mode named '{request.Name}' already exists.");
        }

        var paymentMode = PaymentMode.Create(request.OrganizationId, request.Name);
        db.PaymentModes.Add(paymentMode);
        await db.SaveChangesAsync(cancellationToken);

        return new CreatePaymentModeResult(paymentMode.Id, paymentMode.Name);
    }
}
