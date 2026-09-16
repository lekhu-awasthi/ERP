using ErpApp.Application.Common.Exceptions;
using ErpApp.Application.Common.Persistence;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.Catalog.Queries.ListProductSerials;

public sealed class ListProductSerialsQueryHandler(IAppDbContext db)
    : IRequestHandler<ListProductSerialsQuery, IReadOnlyList<ProductSerialTabRowDto>>
{
    public async Task<IReadOnlyList<ProductSerialTabRowDto>> Handle(
        ListProductSerialsQuery request, CancellationToken cancellationToken)
    {
        var exists = await db.Products
            .AnyAsync(x => x.Id == request.ProductId && x.OrganizationId == request.OrganizationId, cancellationToken);

        if (!exists)
        {
            throw new NotFoundException("Product not found.");
        }

        var layers = db.StockLedgerEntries
            .Where(x => x.OrganizationId == request.OrganizationId && x.ProductId == request.ProductId
                && x.SerialNo != null && x.QuantityRemaining > 0);

        if (request.WarehouseId is { } warehouseId)
        {
            layers = layers.Where(x => x.WarehouseId == warehouseId);
        }

        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            var term = request.Search.Trim();
            layers = layers.Where(x => x.SerialNo!.Contains(term));
        }

        var rows = await layers
            .Select(x => new { x.SerialNo, x.WarehouseId, x.CreatedAt })
            .ToListAsync(cancellationToken);

        var warehouseIds = rows.Select(x => x.WarehouseId).Distinct().ToList();
        var warehouses = await db.Warehouses
            .Where(x => x.OrganizationId == request.OrganizationId && warehouseIds.Contains(x.Id))
            .ToDictionaryAsync(x => x.Id, x => x.Name, cancellationToken);

        return rows
            .Select(x => new ProductSerialTabRowDto(
                x.SerialNo!, x.WarehouseId, warehouses.GetValueOrDefault(x.WarehouseId) ?? string.Empty, x.CreatedAt))
            .OrderBy(x => x.SerialNo, StringComparer.Ordinal)
            .ToList();
    }
}
