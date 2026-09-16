using ErpApp.Application.Common.Exceptions;
using ErpApp.Application.Common.Persistence;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.Catalog.Queries.ListProductBatches;

public sealed class ListProductBatchesQueryHandler(IAppDbContext db)
    : IRequestHandler<ListProductBatchesQuery, IReadOnlyList<ProductBatchTabRowDto>>
{
    public async Task<IReadOnlyList<ProductBatchTabRowDto>> Handle(
        ListProductBatchesQuery request, CancellationToken cancellationToken)
    {
        var product = await db.Products
            .Where(x => x.Id == request.ProductId && x.OrganizationId == request.OrganizationId)
            .Select(x => new { x.Id, x.PrimaryUnitId })
            .SingleOrDefaultAsync(cancellationToken)
            ?? throw new NotFoundException("Product not found.");

        var unitName = await db.UnitsOfMeasurement
            .Where(x => x.Id == product.PrimaryUnitId && x.OrganizationId == request.OrganizationId)
            .Select(x => x.Name)
            .SingleOrDefaultAsync(cancellationToken) ?? string.Empty;

        var batches = db.ProductBatches
            .Where(x => x.OrganizationId == request.OrganizationId && x.ProductId == request.ProductId);

        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            var term = request.Search.Trim();

            // String.Contains, not EF.Functions.Like: InMemory cannot translate Like, and SQL Server
            // turns Contains into the same LIKE. Single-argument Contains is case-INsensitive on SQL
            // Server (collation) and case-sensitive on InMemory, so a handler test must search with
            // the stored casing or it pins a behaviour production does not have (CLAUDE.md).
            batches = batches.Where(x => x.BatchNo.Contains(term));
        }

        var rows = await batches
            .Select(x => new { x.Id, x.BatchNo, x.ManufactureDate, x.ExpiryDate })
            .ToListAsync(cancellationToken);

        if (rows.Count == 0)
        {
            return [];
        }

        var batchIds = rows.Select(x => x.Id).ToList();

        var layers = db.StockLedgerEntries
            .Where(x => x.OrganizationId == request.OrganizationId && x.BatchId != null
                && batchIds.Contains(x.BatchId.Value));

        if (request.WarehouseId is { } warehouseId)
        {
            layers = layers.Where(x => x.WarehouseId == warehouseId);
        }

        var quantities = await layers
            .Select(x => new { BatchId = x.BatchId!.Value, x.QuantityRemaining })
            .ToListAsync(cancellationToken);

        // Summed in memory after ToListAsync: a store-side aggregate over an already-projected
        // record is untranslatable, InMemory evaluates it in C# so every handler test passes, and
        // only an E2E sees the 500 (phase 42).
        var byBatch = quantities
            .GroupBy(x => x.BatchId)
            .ToDictionary(g => g.Key, g => g.Sum(x => x.QuantityRemaining));

        var warehouseName = request.WarehouseId is null
            ? null
            : await db.Warehouses
                .Where(x => x.Id == request.WarehouseId && x.OrganizationId == request.OrganizationId)
                .Select(x => x.Name)
                .SingleOrDefaultAsync(cancellationToken);

        return rows
            .Select(x => new ProductBatchTabRowDto(
                x.Id, x.BatchNo, x.ManufactureDate, x.ExpiryDate,
                byBatch.GetValueOrDefault(x.Id), unitName,
                request.WarehouseId, warehouseName))
            .OrderBy(x => x.ExpiryDate ?? DateOnly.MaxValue)
            .ThenBy(x => x.BatchNo, StringComparer.Ordinal)
            .ToList();
    }
}
