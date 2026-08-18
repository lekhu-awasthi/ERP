using ErpApp.Application.Common.Exceptions;
using ErpApp.Application.Common.Persistence;
using ErpApp.Domain.Crm;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.Crm.Commands.MoveDealToStage;

public sealed class MoveDealToStageCommandHandler(IAppDbContext db)
    : IRequestHandler<MoveDealToStageCommand, MoveDealToStageResult>
{
    public async Task<MoveDealToStageResult> Handle(MoveDealToStageCommand request, CancellationToken cancellationToken)
    {
        var deal = await db.Deals.SingleOrDefaultAsync(
            x => x.Id == request.Id && x.OrganizationId == request.OrganizationId, cancellationToken)
            ?? throw new NotFoundException("Deal not found.");

        if (deal.Status != DealStatus.Pending)
        {
            throw new ConflictException($"A {deal.Status} deal can no longer be edited.");
        }

        await CrmValidation.EnsureDealStageExistsAsync(db, request.OrganizationId, request.DealStageId, cancellationToken);

        deal.MoveToStage(request.DealStageId);
        await db.SaveChangesAsync(cancellationToken);

        return new MoveDealToStageResult(deal.Id, request.DealStageId, deal.Status);
    }
}
