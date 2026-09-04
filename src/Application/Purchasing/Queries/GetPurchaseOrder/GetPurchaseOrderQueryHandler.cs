using ErpApp.Application.Common.Exceptions;
using ErpApp.Application.Common.Persistence;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.Purchasing.Queries.GetPurchaseOrder;

public sealed class GetPurchaseOrderQueryHandler(IAppDbContext db) : IRequestHandler<GetPurchaseOrderQuery, PurchaseOrderDetailDto>
{
    public async Task<PurchaseOrderDetailDto> Handle(GetPurchaseOrderQuery request, CancellationToken cancellationToken)
    {
        var purchaseOrder = await db.PurchaseOrders
            .Include(x => x.Lines)
            .SingleOrDefaultAsync(x => x.Id == request.Id && x.OrganizationId == request.OrganizationId, cancellationToken)
            ?? throw new NotFoundException("Purchase order not found.");

        return new PurchaseOrderDetailDto(
            purchaseOrder.Id,
            purchaseOrder.OrganizationId,
            purchaseOrder.ContactId,
            purchaseOrder.Code,
            purchaseOrder.Date,
            purchaseOrder.Reference,
            purchaseOrder.Status,
            purchaseOrder.ApprovedByUserId,
            purchaseOrder.ApprovedAt,
            purchaseOrder.CreatedAt,
            purchaseOrder.DiscountPct,
            purchaseOrder.CustomStatusId,
            purchaseOrder.Terms,
            purchaseOrder.Lines.Select(x => new PurchaseOrderLineDto(
                x.Id, x.ProductId, x.Quantity, x.Rate, x.VatRate, x.DiscountPct, x.Amount, x.VatAmount)).ToList(),
            purchaseOrder.CurrencyCode,
            purchaseOrder.ExchangeRate);
    }
}
