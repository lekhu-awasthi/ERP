using ErpApp.Application.Common.Exceptions;
using ErpApp.Application.Common.Persistence;
using ErpApp.Domain.Crm;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.Crm.Commands.MarkDealLost;

public sealed class MarkDealLostCommandHandler(IAppDbContext db) : IRequestHandler<MarkDealLostCommand, MarkDealLostResult>
{
    public async Task<MarkDealLostResult> Handle(MarkDealLostCommand request, CancellationToken cancellationToken)
    {
        var deal = await db.Deals.SingleOrDefaultAsync(
            x => x.Id == request.Id && x.OrganizationId == request.OrganizationId, cancellationToken)
            ?? throw new NotFoundException("Deal not found.");

        if (deal.Status != DealStatus.Pending)
        {
            throw new ConflictException($"A {deal.Status} deal can no longer be edited.");
        }

        deal.MarkLost();
        await db.SaveChangesAsync(cancellationToken);

        return new MarkDealLostResult(deal.Id, deal.Status, deal.ClosingDate);
    }
}
