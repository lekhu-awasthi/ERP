using ErpApp.Application.Common.Exceptions;
using ErpApp.Application.Common.Persistence;
using ErpApp.Domain.Payments;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.Payments.Commands.TransitionChequeStatus;

public sealed class TransitionChequeStatusCommandHandler(IAppDbContext db)
    : IRequestHandler<TransitionChequeStatusCommand, TransitionChequeStatusResult>
{
    private static readonly Dictionary<ChequeStatus, ChequeStatus[]> AllowedTransitions = new()
    {
        [ChequeStatus.Pending] = [ChequeStatus.Deposited, ChequeStatus.Cleared, ChequeStatus.Bounced, ChequeStatus.Cancelled],
        [ChequeStatus.Deposited] = [ChequeStatus.Cleared, ChequeStatus.Bounced, ChequeStatus.Cancelled],
        [ChequeStatus.Cleared] = [],
        [ChequeStatus.Bounced] = [],
        [ChequeStatus.Cancelled] = [],
    };

    public async Task<TransitionChequeStatusResult> Handle(TransitionChequeStatusCommand request, CancellationToken cancellationToken)
    {
        var cheque = await db.Cheques.SingleOrDefaultAsync(
            x => x.Id == request.Id && x.OrganizationId == request.OrganizationId, cancellationToken)
            ?? throw new NotFoundException("Cheque not found.");

        if (!AllowedTransitions[cheque.Status].Contains(request.NewStatus))
        {
            throw new ConflictException($"Cannot move a cheque from {cheque.Status} to {request.NewStatus}.");
        }

        cheque.TransitionStatus(request.NewStatus);
        await db.SaveChangesAsync(cancellationToken);

        return new TransitionChequeStatusResult(cheque.Id, cheque.Status);
    }
}
