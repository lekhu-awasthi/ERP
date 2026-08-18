using ErpApp.Application.Common.Exceptions;
using ErpApp.Application.Common.Persistence;
using ErpApp.Domain.Crm;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.Crm.Commands.MarkDealWon;

public sealed class MarkDealWonCommandHandler(IAppDbContext db) : IRequestHandler<MarkDealWonCommand, MarkDealWonResult>
{
    public async Task<MarkDealWonResult> Handle(MarkDealWonCommand request, CancellationToken cancellationToken)
    {
        var deal = await db.Deals.SingleOrDefaultAsync(
            x => x.Id == request.Id && x.OrganizationId == request.OrganizationId, cancellationToken)
            ?? throw new NotFoundException("Deal not found.");

        if (deal.Status != DealStatus.Pending)
        {
            throw new ConflictException($"A {deal.Status} deal can no longer be edited.");
        }

        deal.MarkWon();
        await db.SaveChangesAsync(cancellationToken);

        return new MarkDealWonResult(deal.Id, deal.Status, deal.ClosingDate);
    }
}
