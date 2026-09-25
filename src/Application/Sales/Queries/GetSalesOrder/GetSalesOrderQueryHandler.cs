using ErpApp.Application.Inventory.Stock;
using ErpApp.Application.Common.Exceptions;
using ErpApp.Application.Common.Persistence;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.Sales.Queries.GetSalesOrder;

public sealed class GetSalesOrderQueryHandler(IAppDbContext db) : IRequestHandler<GetSalesOrderQuery, SalesOrderDetailDto>
{
    public async Task<SalesOrderDetailDto> Handle(GetSalesOrderQuery request, CancellationToken cancellationToken)
    {
        var salesOrder = await db.SalesOrders
            .Include(x => x.Lines)
            .SingleOrDefaultAsync(x => x.Id == request.Id && x.OrganizationId == request.OrganizationId, cancellationToken)
            ?? throw new NotFoundException("Sales order not found.");

        // Phase 52 -- the unit each line names, read back through the one shared reader so the
        // eight detail queries cannot drift in how they answer the same question.
        var unitNames = await DocumentLineUnitResolver.LoadUnitNamesAsync(
            db, request.OrganizationId, salesOrder.Lines.Select(x => x.UnitId), cancellationToken);

        return new SalesOrderDetailDto(
            salesOrder.Id,
            salesOrder.OrganizationId,
            salesOrder.ContactId,
            salesOrder.Code,
            salesOrder.Date,
            salesOrder.DeliveryDate,
            salesOrder.Reference,
            salesOrder.Status,
            salesOrder.ApprovedByUserId,
            salesOrder.ApprovedAt,
            salesOrder.CreatedAt,
            salesOrder.DiscountPct,
            salesOrder.Terms,
            salesOrder.Lines.Select(x => new SalesOrderLineDto(
                x.Id, x.ProductId, x.Quantity, x.Rate, x.VatRate, x.DiscountPct, x.Amount, x.VatAmount,
                x.UnitId, x.UnitId is null ? null : unitNames.GetValueOrDefault(x.UnitId.Value), x.ConversionFactor)).ToList(),
            salesOrder.CurrencyCode,
            salesOrder.ExchangeRate,
            salesOrder.LocationId,
            salesOrder.DeliveredAt);
    }
}
