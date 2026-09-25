using ErpApp.Application.Inventory.Stock;
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

        // Phase 52 -- the unit each line names, read back through the one shared reader so the
        // eight detail queries cannot drift in how they answer the same question.
        var unitNames = await DocumentLineUnitResolver.LoadUnitNamesAsync(
            db, request.OrganizationId, purchaseOrder.Lines.Select(x => x.UnitId), cancellationToken);

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
                x.Id, x.ProductId, x.Quantity, x.Rate, x.VatRate, x.DiscountPct, x.Amount, x.VatAmount,
                x.UnitId, x.UnitId is null ? null : unitNames.GetValueOrDefault(x.UnitId.Value), x.ConversionFactor)).ToList(),
            purchaseOrder.CurrencyCode,
            purchaseOrder.ExchangeRate,
            purchaseOrder.LocationId,
            purchaseOrder.ReceivedAt);
    }
}
