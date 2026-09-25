using ErpApp.Application.Common.Exceptions;
using ErpApp.Application.Common.Locations;
using ErpApp.Application.Common.Persistence;
using ErpApp.Application.Inventory.Stock;
using ErpApp.Domain.Common;
using ErpApp.Domain.Purchasing;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.Purchasing.Commands.UpdateGoodsReceivedNote;

public sealed class UpdateGoodsReceivedNoteCommandHandler(IAppDbContext db)
    : IRequestHandler<UpdateGoodsReceivedNoteCommand, UpdateGoodsReceivedNoteResult>
{
    public async Task<UpdateGoodsReceivedNoteResult> Handle(
        UpdateGoodsReceivedNoteCommand request, CancellationToken cancellationToken)
    {
        var goodsReceivedNote = await db.GoodsReceivedNotes
            .Include(x => x.Lines)
            .SingleOrDefaultAsync(x => x.Id == request.Id && x.OrganizationId == request.OrganizationId, cancellationToken)
            ?? throw new NotFoundException("Goods received note not found.");

        if (goodsReceivedNote.Status != GoodsReceivedNoteStatus.Draft)
        {
            throw new ConflictException("Only a Draft goods received note can be edited.");
        }

        await PurchasingValidation.EnsureSupplierExistsAsync(db, request.OrganizationId, request.ContactId, cancellationToken);
        await PurchasingValidation.EnsureWarehouseExistsAsync(db, request.OrganizationId, request.WarehouseId, cancellationToken);
        await PurchasingValidation.EnsureProductsExistAsync(
            db, request.OrganizationId, request.Lines.Select(x => x.ProductId), cancellationToken);

        // A GRN received against a purchase order stays that order's supplier's GRN.
        if (goodsReceivedNote.ReferrerId is not null && request.ContactId != goodsReceivedNote.ContactId)
        {
            throw new ConflictException(
                "A goods received note raised from a purchase order must stay with that order's supplier.");
        }

        var oldLines = goodsReceivedNote.Lines.ToList();

        goodsReceivedNote.UpdateHeader(
            request.ContactId, request.WarehouseId, request.Date, request.Reference, request.TrackingNo, request.DiscountPct);
        goodsReceivedNote.SetCurrency(request.CurrencyCode, request.ExchangeRate);
        goodsReceivedNote.SetLocation(await LocationResolver.ResolveAsync(
            db, request.OrganizationId, DocumentType.GoodsReceivedNote, request.LocationId, cancellationToken));

        goodsReceivedNote.ClearLines();
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

        // Phase-4 bug #1's remedy: replace the lines through the child set, not the collection.
        db.GoodsReceivedNoteLines.RemoveRange(oldLines);
        db.GoodsReceivedNoteLines.AddRange(goodsReceivedNote.Lines);

        await db.SaveChangesAsync(cancellationToken);

        return new UpdateGoodsReceivedNoteResult(goodsReceivedNote.Id, goodsReceivedNote.Code, goodsReceivedNote.Status);
    }
}
