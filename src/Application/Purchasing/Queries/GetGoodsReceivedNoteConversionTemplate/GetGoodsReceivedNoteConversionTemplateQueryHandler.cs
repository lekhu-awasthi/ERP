using ErpApp.Application.Common.Exceptions;
using ErpApp.Application.Common.Persistence;
using ErpApp.Domain.Common;
using ErpApp.Domain.Purchasing;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.Purchasing.Queries.GetGoodsReceivedNoteConversionTemplate;

public sealed class GetGoodsReceivedNoteConversionTemplateQueryHandler(IAppDbContext db)
    : IRequestHandler<GetGoodsReceivedNoteConversionTemplateQuery, GoodsReceivedNoteConversionTemplateDto>
{
    public async Task<GoodsReceivedNoteConversionTemplateDto> Handle(
        GetGoodsReceivedNoteConversionTemplateQuery request, CancellationToken cancellationToken)
    {
        var purchaseOrder = await db.PurchaseOrders
            .Include(x => x.Lines)
            .SingleOrDefaultAsync(x => x.Id == request.PurchaseOrderId && x.OrganizationId == request.OrganizationId, cancellationToken)
            ?? throw new NotFoundException("Purchase order not found.");

        if (purchaseOrder.Status is not (PurchaseOrderStatus.Approved or PurchaseOrderStatus.Converted))
        {
            throw new ConflictException("Only an Approved purchase order can be received.");
        }

        if (purchaseOrder.IsReceived)
        {
            throw new ConflictException("This purchase order has already been received on a Goods Received Note.");
        }

        return new GoodsReceivedNoteConversionTemplateDto(
            purchaseOrder.ContactId,
            NepalTime.LocalDate(DateTimeOffset.UtcNow),
            purchaseOrder.Code,
            DocumentType.PurchaseOrder,
            purchaseOrder.Id,
            purchaseOrder.DiscountPct,
            [.. purchaseOrder.Lines.Select(x => new GoodsReceivedNoteLineInput(
                x.ProductId, x.Quantity, x.Rate, x.VatRate, x.DiscountPct, x.UnitId))],
            purchaseOrder.CurrencyCode,
            purchaseOrder.ExchangeRate,
            purchaseOrder.LocationId);
    }
}
