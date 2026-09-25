using ErpApp.Application.Common.Exceptions;
using ErpApp.Application.Common.Persistence;
using ErpApp.Domain.Common;
using ErpApp.Domain.Sales;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.Sales.Queries.GetDeliveryNoteConversionTemplate;

public sealed class GetDeliveryNoteConversionTemplateQueryHandler(IAppDbContext db)
    : IRequestHandler<GetDeliveryNoteConversionTemplateQuery, DeliveryNoteConversionTemplateDto>
{
    public async Task<DeliveryNoteConversionTemplateDto> Handle(
        GetDeliveryNoteConversionTemplateQuery request, CancellationToken cancellationToken)
    {
        var salesOrder = await db.SalesOrders
            .Include(x => x.Lines)
            .SingleOrDefaultAsync(x => x.Id == request.SalesOrderId && x.OrganizationId == request.OrganizationId, cancellationToken)
            ?? throw new NotFoundException("Sales order not found.");

        if (salesOrder.Status != SalesOrderStatus.Approved)
        {
            throw new ConflictException("Only an Approved sales order can be delivered.");
        }

        if (salesOrder.IsDelivered)
        {
            throw new ConflictException("This sales order has already been delivered on a Delivery Note.");
        }

        var today = NepalTime.LocalDate(DateTimeOffset.UtcNow);

        return new DeliveryNoteConversionTemplateDto(
            salesOrder.ContactId,
            today,
            salesOrder.DeliveryDate ?? today.AddDays(1),
            salesOrder.Code,
            DocumentType.SalesOrder,
            salesOrder.Id,
            salesOrder.DiscountPct,
            salesOrder.Terms,
            [.. salesOrder.Lines.Select(x => new DeliveryNoteLineInput(
                x.ProductId, x.Quantity, x.Rate, x.VatRate, x.DiscountPct, x.UnitId))],
            salesOrder.CurrencyCode,
            salesOrder.ExchangeRate,
            salesOrder.LocationId);
    }
}
