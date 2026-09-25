using ErpApp.Application.Common.Exceptions;
using ErpApp.Application.Common.Persistence;
using ErpApp.Application.Inventory.Stock;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.Sales.Queries.GetDeliveryNote;

public sealed class GetDeliveryNoteQueryHandler(IAppDbContext db)
    : IRequestHandler<GetDeliveryNoteQuery, DeliveryNoteDetailDto>
{
    public async Task<DeliveryNoteDetailDto> Handle(GetDeliveryNoteQuery request, CancellationToken cancellationToken)
    {
        var note = await db.DeliveryNotes
            .Include(x => x.Lines)
            .SingleOrDefaultAsync(x => x.Id == request.Id && x.OrganizationId == request.OrganizationId, cancellationToken)
            ?? throw new NotFoundException("Delivery note not found.");

        var unitNames = await DocumentLineUnitResolver.LoadUnitNamesAsync(
            db, request.OrganizationId, note.Lines.Select(x => x.UnitId), cancellationToken);

        var referrerCode = note.ReferrerId is { } salesOrderId
            ? await db.SalesOrders
                .Where(x => x.Id == salesOrderId && x.OrganizationId == request.OrganizationId)
                .Select(x => x.Code)
                .SingleOrDefaultAsync(cancellationToken)
            : null;

        return new DeliveryNoteDetailDto(
            note.Id,
            note.OrganizationId,
            note.ContactId,
            note.WarehouseId,
            note.Code,
            note.Date,
            note.ExpectedDeliveryDate,
            note.Reference,
            note.TrackingNo,
            note.ShippingAddress,
            note.Status,
            note.ApprovedByUserId,
            note.ApprovedAt,
            note.VoidedAt,
            note.CreatedAt,
            note.DiscountPct,
            note.CustomStatusId,
            note.Terms,
            note.ReferrerType,
            note.ReferrerId,
            referrerCode,
            note.Lines.Select(x => new DeliveryNoteLineDto(
                x.Id, x.ProductId, x.Quantity, x.Rate, x.VatRate, x.DiscountPct, x.Amount, x.VatAmount,
                x.UnitId, x.UnitId is null ? null : unitNames.GetValueOrDefault(x.UnitId.Value), x.ConversionFactor)).ToList(),
            note.CurrencyCode,
            note.ExchangeRate,
            note.LocationId);
    }
}
