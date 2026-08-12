using ErpApp.Application.Common.Exceptions;
using ErpApp.Application.Common.Numbering;
using ErpApp.Application.Common.Persistence;
using ErpApp.Application.Common.Security;
using ErpApp.Domain.Common;
using ErpApp.Domain.Purchasing;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.Purchasing.Commands.ApprovePurchaseOrder;

public sealed class ApprovePurchaseOrderCommandHandler(
    IAppDbContext db, IDocumentNumberGenerator numberGenerator, ICurrentUserService currentUser)
    : IRequestHandler<ApprovePurchaseOrderCommand, ApprovePurchaseOrderResult>
{
    public async Task<ApprovePurchaseOrderResult> Handle(ApprovePurchaseOrderCommand request, CancellationToken cancellationToken)
    {
        var purchaseOrder = await db.PurchaseOrders
            .Include(x => x.Lines)
            .SingleOrDefaultAsync(x => x.Id == request.Id && x.OrganizationId == request.OrganizationId, cancellationToken)
            ?? throw new NotFoundException("Purchase order not found.");

        if (purchaseOrder.Status != PurchaseOrderStatus.Draft)
        {
            throw new ConflictException("Only a Draft purchase order can be approved.");
        }

        if (purchaseOrder.Lines.Count == 0)
        {
            throw new ConflictException("A purchase order needs at least one line to be approved.");
        }

        var code = await numberGenerator.GetNextNumberAsync(request.OrganizationId, DocumentType.PurchaseOrder, cancellationToken);

        purchaseOrder.Approve(currentUser.UserId, code);

        await db.SaveChangesAsync(cancellationToken);

        return new ApprovePurchaseOrderResult(purchaseOrder.Id, purchaseOrder.Code, purchaseOrder.Status, purchaseOrder.ApprovedAt);
    }
}
