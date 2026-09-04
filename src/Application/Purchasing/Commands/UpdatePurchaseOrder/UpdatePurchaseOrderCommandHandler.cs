using ErpApp.Application.Common.Exceptions;
using ErpApp.Application.Common.Persistence;
using ErpApp.Domain.Purchasing;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.Purchasing.Commands.UpdatePurchaseOrder;

public sealed class UpdatePurchaseOrderCommandHandler(IAppDbContext db)
    : IRequestHandler<UpdatePurchaseOrderCommand, UpdatePurchaseOrderResult>
{
    public async Task<UpdatePurchaseOrderResult> Handle(UpdatePurchaseOrderCommand request, CancellationToken cancellationToken)
    {
        var purchaseOrder = await db.PurchaseOrders
            .Include(x => x.Lines)
            .SingleOrDefaultAsync(x => x.Id == request.Id && x.OrganizationId == request.OrganizationId, cancellationToken)
            ?? throw new NotFoundException("Purchase order not found.");

        if (purchaseOrder.Status != PurchaseOrderStatus.Draft)
        {
            throw new ConflictException("Only a Draft purchase order can be edited.");
        }

        await PurchasingValidation.EnsureSupplierExistsAsync(db, request.OrganizationId, request.ContactId, cancellationToken);
        await PurchasingValidation.EnsureProductsExistAsync(
            db, request.OrganizationId, request.Lines.Select(x => x.ProductId), cancellationToken);

        var oldLines = purchaseOrder.Lines.ToList();

        purchaseOrder.UpdateHeader(request.ContactId, request.Date, request.Reference, request.DiscountPct);

        // Phase 28 -- see the Create handler's note. Draft-only, enforced by the aggregate.
        purchaseOrder.SetCurrency(request.CurrencyCode, request.ExchangeRate);
        purchaseOrder.SetTerms(request.Terms);

        purchaseOrder.ClearLines();
        foreach (var line in request.Lines)
        {
            purchaseOrder.AddLine(line.ProductId, line.Quantity, line.Rate, line.VatRate, line.DiscountPct);
        }

        db.PurchaseOrderLines.RemoveRange(oldLines);
        db.PurchaseOrderLines.AddRange(purchaseOrder.Lines);

        await db.SaveChangesAsync(cancellationToken);

        return new UpdatePurchaseOrderResult(purchaseOrder.Id, purchaseOrder.Code, purchaseOrder.Status);
    }
}
