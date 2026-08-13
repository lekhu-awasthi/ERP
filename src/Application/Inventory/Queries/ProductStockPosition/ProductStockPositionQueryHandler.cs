using ErpApp.Application.Common.Persistence;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.Inventory.Queries.ProductStockPosition;

public sealed class ProductStockPositionQueryHandler(IAppDbContext db)
    : IRequestHandler<ProductStockPositionQuery, IReadOnlyList<StockPositionDto>>
{
    public async Task<IReadOnlyList<StockPositionDto>> Handle(
        ProductStockPositionQuery request, CancellationToken cancellationToken)
    {
        var query = db.StockLedgerEntries.Where(x => x.OrganizationId == request.OrganizationId);

        if (request.ProductId is { } productId)
        {
            query = query.Where(x => x.ProductId == productId);
        }

        if (request.WarehouseId is { } warehouseId)
        {
            query = query.Where(x => x.WarehouseId == warehouseId);
        }

        var positions = await query
            .GroupBy(x => new { x.ProductId, x.WarehouseId })
            .Select(g => new
            {
                g.Key.ProductId,
                g.Key.WarehouseId,
                In = g.Sum(x => x.QuantityIn),
                Balance = g.Sum(x => x.QuantityRemaining),
            })
            .ToListAsync(cancellationToken);

        return positions
            .Select(x => new StockPositionDto(x.ProductId, x.WarehouseId, Opening: 0m, x.In, Out: x.In - x.Balance, x.Balance))
            .OrderBy(x => x.ProductId)
            .ThenBy(x => x.WarehouseId)
            .ToList();
    }
}
