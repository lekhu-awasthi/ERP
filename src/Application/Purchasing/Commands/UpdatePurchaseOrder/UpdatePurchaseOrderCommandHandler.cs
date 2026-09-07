using ErpApp.Application.Common.Exceptions;
using ErpApp.Application.Common.Locations;
using ErpApp.Application.Common.Persistence;
using ErpApp.Domain.Common;
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

        // Phase 32 -- same treatment as the currency pair above: resolved right after
        // construction rather than threaded through Create's parameter list. Null means
        // "the tenant's default", and LocationResolver returns a real null when this type is
        // outside the tenant's LocationScopeMode, so a client that keeps sending a location
        // after an Admin narrows the scope cannot quietly keep writing one.
        purchaseOrder.SetLocation(await LocationResolver.ResolveAsync(
            db, request.OrganizationId, DocumentType.PurchaseOrder, request.LocationId,
            cancellationToken));
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
