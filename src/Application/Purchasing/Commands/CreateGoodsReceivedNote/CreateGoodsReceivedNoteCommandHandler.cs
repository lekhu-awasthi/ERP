using ErpApp.Application.Common.Exceptions;
using ErpApp.Application.Common.Locations;
using ErpApp.Application.Common.Persistence;
using ErpApp.Application.Inventory.Stock;
using ErpApp.Domain.Common;
using ErpApp.Domain.Purchasing;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.Purchasing.Commands.CreateGoodsReceivedNote;

public sealed class CreateGoodsReceivedNoteCommandHandler(IAppDbContext db)
    : IRequestHandler<CreateGoodsReceivedNoteCommand, CreateGoodsReceivedNoteResult>
{
    public async Task<CreateGoodsReceivedNoteResult> Handle(
        CreateGoodsReceivedNoteCommand request, CancellationToken cancellationToken)
    {
        await PurchasingValidation.EnsureSupplierExistsAsync(db, request.OrganizationId, request.ContactId, cancellationToken);
        await PurchasingValidation.EnsureWarehouseExistsAsync(db, request.OrganizationId, request.WarehouseId, cancellationToken);
        await PurchasingValidation.EnsureProductsExistAsync(
            db, request.OrganizationId, request.Lines.Select(x => x.ProductId), cancellationToken);

        // Phase 6's rule: ReferrerType/ReferrerId enforce nothing by themselves, so the conversion's
        // checks live here. The order must be approved (or already billed -- receiving and billing
        // are independent), not already received, and for the same supplier.
        if (request.ReferrerType == DocumentType.PurchaseOrder && request.ReferrerId is { } purchaseOrderId)
        {
            var purchaseOrder = await db.PurchaseOrders.SingleOrDefaultAsync(
                x => x.Id == purchaseOrderId && x.OrganizationId == request.OrganizationId, cancellationToken)
                ?? throw new NotFoundException("Purchase order not found.");

            if (purchaseOrder.Status is not (PurchaseOrderStatus.Approved or PurchaseOrderStatus.Converted))
            {
                throw new ConflictException("Only an Approved purchase order can be received.");
            }

            if (purchaseOrder.IsReceived)
            {
                throw new ConflictException("This purchase order has already been received on a Goods Received Note.");
            }

            if (purchaseOrder.ContactId != request.ContactId)
            {
                throw new ConflictException("A goods received note must be from the same supplier as its purchase order.");
            }

            purchaseOrder.MarkReceived();
        }

        var goodsReceivedNote = GoodsReceivedNote.Create(
            request.OrganizationId, request.ContactId, request.WarehouseId, request.Date, request.Reference,
            request.TrackingNo, request.ReferrerType, request.ReferrerId, request.DiscountPct);

        goodsReceivedNote.SetCurrency(request.CurrencyCode, request.ExchangeRate);
        goodsReceivedNote.SetLocation(await LocationResolver.ResolveAsync(
            db, request.OrganizationId, DocumentType.GoodsReceivedNote, request.LocationId, cancellationToken));

        // Phase 52 -- the unit each line names becomes the factor frozen onto it, once.
        var units = await DocumentLineUnitResolver.ResolveAsync(
            db, request.OrganizationId,
            [.. request.Lines.Select(x => new DocumentLineUnitResolver.LineUnitInput(x.ProductId, x.UnitId))],
            cancellationToken);

        for (var i = 0; i < request.Lines.Count; i++)
        {
            var line = request.Lines[i];
            goodsReceivedNote.AddLine(
                line.ProductId, line.Quantity, line.Rate, line.VatRate, line.DiscountPct,
                units[i].UnitId, units[i].ConversionFactor);
        }

        db.GoodsReceivedNotes.Add(goodsReceivedNote);
        await db.SaveChangesAsync(cancellationToken);

        return new CreateGoodsReceivedNoteResult(goodsReceivedNote.Id, goodsReceivedNote.Code, goodsReceivedNote.Status);
    }
}
