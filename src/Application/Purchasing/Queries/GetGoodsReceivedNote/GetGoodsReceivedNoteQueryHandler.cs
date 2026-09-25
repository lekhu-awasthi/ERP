using ErpApp.Application.Common.Exceptions;
using ErpApp.Application.Common.Persistence;
using ErpApp.Application.Inventory.Stock;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.Purchasing.Queries.GetGoodsReceivedNote;

public sealed class GetGoodsReceivedNoteQueryHandler(IAppDbContext db)
    : IRequestHandler<GetGoodsReceivedNoteQuery, GoodsReceivedNoteDetailDto>
{
    public async Task<GoodsReceivedNoteDetailDto> Handle(
        GetGoodsReceivedNoteQuery request, CancellationToken cancellationToken)
    {
        var note = await db.GoodsReceivedNotes
            .Include(x => x.Lines)
            .SingleOrDefaultAsync(x => x.Id == request.Id && x.OrganizationId == request.OrganizationId, cancellationToken)
            ?? throw new NotFoundException("Goods received note not found.");

        var unitNames = await DocumentLineUnitResolver.LoadUnitNamesAsync(
            db, request.OrganizationId, note.Lines.Select(x => x.UnitId), cancellationToken);

        var referrerCode = note.ReferrerId is { } purchaseOrderId
            ? await db.PurchaseOrders
                .Where(x => x.Id == purchaseOrderId && x.OrganizationId == request.OrganizationId)
                .Select(x => x.Code)
                .SingleOrDefaultAsync(cancellationToken)
            : null;

        return new GoodsReceivedNoteDetailDto(
            note.Id,
            note.OrganizationId,
            note.ContactId,
            note.WarehouseId,
            note.Code,
            note.Date,
            note.Reference,
            note.TrackingNo,
            note.Status,
            note.ApprovedByUserId,
            note.ApprovedAt,
            note.VoidedAt,
            note.CreatedAt,
            note.DiscountPct,
            note.CustomStatusId,
            note.ReferrerType,
            note.ReferrerId,
            referrerCode,
            note.Lines.Select(x => new GoodsReceivedNoteLineDto(
                x.Id, x.ProductId, x.Quantity, x.Rate, x.VatRate, x.DiscountPct, x.Amount, x.VatAmount,
                x.UnitId, x.UnitId is null ? null : unitNames.GetValueOrDefault(x.UnitId.Value), x.ConversionFactor)).ToList(),
            note.CurrencyCode,
            note.ExchangeRate,
            note.LocationId);
    }
}
