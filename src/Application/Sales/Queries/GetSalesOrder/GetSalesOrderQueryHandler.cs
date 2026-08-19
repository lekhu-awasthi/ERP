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
            salesOrder.Lines.Select(x => new SalesOrderLineDto(
                x.Id, x.ProductId, x.Quantity, x.Rate, x.VatRate, x.DiscountPct, x.Amount, x.VatAmount)).ToList());
    }
}
