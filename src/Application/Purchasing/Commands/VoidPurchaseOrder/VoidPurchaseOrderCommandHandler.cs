using ErpApp.Application.Common.Exceptions;
using ErpApp.Application.Common.Persistence;
using ErpApp.Application.Common.Security;
using ErpApp.Domain.Purchasing;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.Purchasing.Commands.VoidPurchaseOrder;

/// <summary>Mirror of VoidQuotationCommandHandler -- a Converted purchase order is rejected by
/// PurchaseOrder.Void's own EnsureApproved guard. No GL, no stock.</summary>
public sealed class VoidPurchaseOrderCommandHandler(IAppDbContext db, ICurrentUserService currentUser)
    : IRequestHandler<VoidPurchaseOrderCommand, VoidPurchaseOrderResult>
{
    public async Task<VoidPurchaseOrderResult> Handle(VoidPurchaseOrderCommand request, CancellationToken cancellationToken)
    {
        var purchaseOrder = await db.PurchaseOrders.SingleOrDefaultAsync(
            x => x.Id == request.Id && x.OrganizationId == request.OrganizationId, cancellationToken)
            ?? throw new NotFoundException("Purchase order not found.");

        if (purchaseOrder.Status != PurchaseOrderStatus.Approved)
        {
            throw new ConflictException("Only an Approved purchase order can be voided.");
        }

        // Phase 58 -- a received order has a live dependent, its Goods Received Note, exactly as a
        // Converted one has its bill.
        if (purchaseOrder.IsReceived)
        {
            throw new ConflictException(
                "Cannot void this purchase order -- goods have been received against it on a Goods Received Note.");
        }

        purchaseOrder.Void(currentUser.UserId);

        await db.SaveChangesAsync(cancellationToken);

        return new VoidPurchaseOrderResult(purchaseOrder.Id, purchaseOrder.Code, purchaseOrder.Status, purchaseOrder.VoidedAt);
    }
}
