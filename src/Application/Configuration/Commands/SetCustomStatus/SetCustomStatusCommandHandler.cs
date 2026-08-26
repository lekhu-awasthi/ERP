using ErpApp.Application.Common.Exceptions;
using ErpApp.Application.Common.Persistence;
using ErpApp.Domain.Common;
using FluentValidation;
using FluentValidation.Results;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.Configuration.Commands.SetCustomStatus;

public sealed class SetCustomStatusCommandHandler(IAppDbContext db) : IRequestHandler<SetCustomStatusCommand, Unit>
{
    public async Task<Unit> Handle(SetCustomStatusCommand request, CancellationToken cancellationToken)
    {
        if (request.CustomStatusId is { } customStatusId)
        {
            var customStatus = await db.CustomStatuses.SingleOrDefaultAsync(
                x => x.Id == customStatusId && x.OrganizationId == request.OrganizationId, cancellationToken)
                ?? throw new NotFoundException("Custom status not found.");

            if (!customStatus.IsActive)
            {
                throw new ValidationException(
                    [new ValidationFailure(nameof(request.CustomStatusId), $"'{customStatus.Name}' is inactive.")]);
            }

            if (customStatus.DocumentType != request.DocumentType)
            {
                throw new ValidationException(
                    [new ValidationFailure(
                        nameof(request.CustomStatusId),
                        $"'{customStatus.Name}' is not a custom status defined for {request.DocumentType}.")]);
            }
        }

        switch (request.DocumentType)
        {
            case DocumentType.Quotation:
                var quotation = await db.Quotations.SingleOrDefaultAsync(
                    x => x.Id == request.DocumentId && x.OrganizationId == request.OrganizationId, cancellationToken)
                    ?? throw new NotFoundException("Quotation not found.");
                quotation.SetCustomStatus(request.CustomStatusId);
                break;

            case DocumentType.PurchaseOrder:
                var purchaseOrder = await db.PurchaseOrders.SingleOrDefaultAsync(
                    x => x.Id == request.DocumentId && x.OrganizationId == request.OrganizationId, cancellationToken)
                    ?? throw new NotFoundException("Purchase order not found.");
                purchaseOrder.SetCustomStatus(request.CustomStatusId);
                break;

            default:
                throw new ArgumentOutOfRangeException(
                    nameof(request.DocumentType), request.DocumentType, "Custom status is not wired up for this document type yet.");
        }

        await db.SaveChangesAsync(cancellationToken);
        return Unit.Value;
    }
}
