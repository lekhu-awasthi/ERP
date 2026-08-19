using ErpApp.Application.Common.Persistence;
using ErpApp.Domain.Purchasing;
using MediatR;

namespace ErpApp.Application.Purchasing.Commands.CreatePurchaseOrder;

public sealed class CreatePurchaseOrderCommandHandler(IAppDbContext db)
    : IRequestHandler<CreatePurchaseOrderCommand, CreatePurchaseOrderResult>
{
    public async Task<CreatePurchaseOrderResult> Handle(CreatePurchaseOrderCommand request, CancellationToken cancellationToken)
    {
        await PurchasingValidation.EnsureSupplierExistsAsync(db, request.OrganizationId, request.ContactId, cancellationToken);
        await PurchasingValidation.EnsureProductsExistAsync(
            db, request.OrganizationId, request.Lines.Select(x => x.ProductId), cancellationToken);

        var purchaseOrder = PurchaseOrder.Create(
            request.OrganizationId, request.ContactId, request.Date, request.Reference, request.DiscountPct);
        foreach (var line in request.Lines)
        {
            purchaseOrder.AddLine(line.ProductId, line.Quantity, line.Rate, line.VatRate, line.DiscountPct);
        }

        db.PurchaseOrders.Add(purchaseOrder);
        await db.SaveChangesAsync(cancellationToken);

        return new CreatePurchaseOrderResult(purchaseOrder.Id, purchaseOrder.Code, purchaseOrder.Status);
    }
}
