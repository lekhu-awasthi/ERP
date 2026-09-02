using ErpApp.Application.Common.Exceptions;
using ErpApp.Application.Common.Persistence;
using ErpApp.Application.Common.Security;
using ErpApp.Domain.Manufacturing;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.Manufacturing.Commands.VoidProductionOrder;

/// <summary>
/// Nothing to unwind: an approved Production Order moved no stock and posted no GL. The one thing
/// this must not do is void an order that has already been converted, and it does not have to
/// check for that itself -- ProductionOrder.Void only accepts Approved, and a converted order is
/// Converted.
/// </summary>
public sealed class VoidProductionOrderCommandHandler(IAppDbContext db, ICurrentUserService currentUser)
    : IRequestHandler<VoidProductionOrderCommand, VoidProductionOrderResult>
{
    public async Task<VoidProductionOrderResult> Handle(
        VoidProductionOrderCommand request, CancellationToken cancellationToken)
    {
        var order = await db.ProductionOrders
            .SingleOrDefaultAsync(x => x.Id == request.Id && x.OrganizationId == request.OrganizationId, cancellationToken)
            ?? throw new NotFoundException("Production order not found.");

        if (order.Status != ProductionOrderStatus.Approved)
        {
            throw new ConflictException(
                "Only an Approved production order can be voided. An order already converted to a Production Journal " +
                "cannot be voided -- void the journal instead.");
        }

        order.Void(currentUser.UserId);
        await db.SaveChangesAsync(cancellationToken);

        return new VoidProductionOrderResult(order.Id, order.Code, order.Status, order.VoidedAt);
    }
}
