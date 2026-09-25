using ErpApp.Application.Common.Exceptions;
using ErpApp.Application.Common.Persistence;
using ErpApp.Application.Common.Security;
using ErpApp.Domain.Sales;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.Sales.Commands.VoidSalesOrder;

/// <summary>No GL, no stock, no dependents -- SalesOrder is standalone (see SalesOrder's own doc
/// comment). Ships backend-only, same as its Create/Update/Approve -- no Angular UI this phase.</summary>
public sealed class VoidSalesOrderCommandHandler(IAppDbContext db, ICurrentUserService currentUser)
    : IRequestHandler<VoidSalesOrderCommand, VoidSalesOrderResult>
{
    public async Task<VoidSalesOrderResult> Handle(VoidSalesOrderCommand request, CancellationToken cancellationToken)
    {
        var salesOrder = await db.SalesOrders.SingleOrDefaultAsync(
            x => x.Id == request.Id && x.OrganizationId == request.OrganizationId, cancellationToken)
            ?? throw new NotFoundException("Sales order not found.");

        if (salesOrder.Status != SalesOrderStatus.Approved)
        {
            throw new ConflictException("Only an Approved sales order can be voided.");
        }

        // Phase 58 -- a delivered order has a live dependent, its Delivery Note.
        if (salesOrder.IsDelivered)
        {
            throw new ConflictException(
                "Cannot void this sales order -- goods have been delivered against it on a Delivery Note.");
        }

        salesOrder.Void(currentUser.UserId);

        await db.SaveChangesAsync(cancellationToken);

        return new VoidSalesOrderResult(salesOrder.Id, salesOrder.Code, salesOrder.Status, salesOrder.VoidedAt);
    }
}
