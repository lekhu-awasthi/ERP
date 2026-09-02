using ErpApp.Application.Common.Exceptions;
using ErpApp.Application.Common.Persistence;
using ErpApp.Domain.Common;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.Manufacturing.Queries.GetProductionOrder;

public sealed class GetProductionOrderQueryHandler(IAppDbContext db)
    : IRequestHandler<GetProductionOrderQuery, ProductionOrderDetailDto>
{
    public async Task<ProductionOrderDetailDto> Handle(
        GetProductionOrderQuery request, CancellationToken cancellationToken)
    {
        var order = await db.ProductionOrders
            .Include(x => x.RawMaterials).Include(x => x.ByProducts).Include(x => x.Expenses)
            .SingleOrDefaultAsync(x => x.Id == request.Id && x.OrganizationId == request.OrganizationId, cancellationToken)
            ?? throw new NotFoundException("Production order not found.");

        var productIds = new List<Guid> { order.ProductId };
        productIds.AddRange(order.RawMaterials.Select(x => x.ProductId));
        productIds.AddRange(order.ByProducts.Select(x => x.ProductId));
        var products = await ProductLabels.LoadAsync(db, request.OrganizationId, productIds, cancellationToken);

        var costTermIds = order.Expenses.Select(x => x.CostTermId).Distinct().ToList();
        var costTerms = await db.CostTerms
            .Where(x => x.OrganizationId == request.OrganizationId && costTermIds.Contains(x.Id))
            .ToDictionaryAsync(x => x.Id, x => x.Name, cancellationToken);

        // The journal this order became, if any -- so the detail page can link to it instead of
        // offering a Convert action that would be refused.
        var converted = await db.ProductionJournals
            .Where(x => x.OrganizationId == request.OrganizationId
                && x.ReferrerType == DocumentType.ProductionOrder && x.ReferrerId == order.Id)
            .Select(x => new { x.Id, x.Code })
            .FirstOrDefaultAsync(cancellationToken);

        var finished = products.GetValueOrDefault(order.ProductId);

        return new ProductionOrderDetailDto(
            order.Id,
            order.Code,
            order.Date,
            order.Reference,
            order.ProductId,
            finished?.Name ?? string.Empty,
            finished?.Code ?? string.Empty,
            finished?.UnitName,
            order.OutputQuantity,
            order.BillOfMaterialsId,
            order.Notes,
            order.Status,
            converted?.Id,
            converted?.Code,
            order.ApprovedAt,
            order.VoidedAt,
            order.CreatedAt,
            [.. order.RawMaterials.Select(line =>
            {
                var product = products.GetValueOrDefault(line.ProductId);
                return new ProductionOrderRawMaterialLineDto(
                    line.Id, line.ProductId, product?.Name ?? string.Empty, product?.Code ?? string.Empty,
                    product?.UnitName, line.Quantity);
            })],
            [.. order.ByProducts.Select(line =>
            {
                var product = products.GetValueOrDefault(line.ProductId);
                return new ProductionOrderByProductLineDto(
                    line.Id, line.ProductId, product?.Name ?? string.Empty, product?.Code ?? string.Empty,
                    product?.UnitName, line.CostAllocationPct, line.Quantity);
            })],
            [.. order.Expenses.Select(line => new ProductionOrderExpenseLineDto(
                line.Id, line.CostTermId, costTerms.GetValueOrDefault(line.CostTermId) ?? string.Empty, line.Amount))]);
    }
}
