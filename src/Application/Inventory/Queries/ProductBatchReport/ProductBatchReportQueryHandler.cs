using ErpApp.Application.Common.Locations;
using ErpApp.Application.Common.Persistence;
using ErpApp.Application.Common.Security;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.Inventory.Queries.ProductBatchReport;

/// <summary>
/// See the query's doc comment for what was read and what was derived.
///
/// <para><b>The shape of the work.</b> Batch rows are FIFO layers grouped by batch (and, when asked,
/// by warehouse). Grouping is done in memory after <c>ToListAsync</c> rather than store-side, and
/// that is deliberate: phase 42's lesson is that a store-side aggregate over an already-projected
/// record is untranslatable, InMemory evaluates it in C# so every handler test passes, and only an
/// E2E sees the 500. The set being grouped is one tenant's layers for batch-tracked products, which
/// is bounded by how many batches a tenant actually runs -- not by its transaction volume.</para>
/// </summary>
public sealed class ProductBatchReportQueryHandler(IAppDbContext db, ICurrentUserService currentUser)
    : IRequestHandler<ProductBatchReportQuery, ProductBatchReportDto>
{
    public async Task<ProductBatchReportDto> Handle(
        ProductBatchReportQuery request, CancellationToken cancellationToken)
    {
        // Phase 35b -- a Reports.* key cannot be granted per location, so the location scope comes
        // from the caller's own transaction grants. Null means "every location this caller may see".
        var allowedLocations = await LocationAccessScope.ForReportsAsync(
            db, currentUser, request.OrganizationId, cancellationToken);

        var layers = db.StockLedgerEntries
            .Where(x => x.OrganizationId == request.OrganizationId && x.BatchId != null
                && x.TransactionDate >= request.FromDate && x.TransactionDate <= request.ToDate);

        // Composed as separate Where calls, never folded into one predicate: an expression tree does
        // not short-circuit, so `id == null || x.Col == id` is evaluated per row (CLAUDE.md).
        if (request.ProductId is { } productId)
        {
            layers = layers.Where(x => x.ProductId == productId);
        }

        if (request.WarehouseId is { } warehouseId)
        {
            layers = layers.Where(x => x.WarehouseId == warehouseId);
        }

        if (request.LocationId is { } locationId)
        {
            layers = layers.Where(x => x.LocationId == locationId);
        }
        else if (allowedLocations is not null)
        {
            layers = layers.Where(x => x.LocationId == null || allowedLocations.Contains(x.LocationId.Value));
        }

        var rows = await layers
            .Join(db.ProductBatches.Where(b => b.OrganizationId == request.OrganizationId),
                l => l.BatchId, b => b.Id,
                (l, b) => new { l.ProductId, l.WarehouseId, l.QuantityRemaining, b.Id, b.BatchNo, b.ManufactureDate, b.ExpiryDate })
            .ToListAsync(cancellationToken);

        var groupByWarehouse = request.GroupBy == ProductBatchGroupBy.Warehouse;

        var grouped = rows
            .GroupBy(x => (x.Id, WarehouseId: groupByWarehouse ? x.WarehouseId : (Guid?)null))
            .Select(g => new
            {
                BatchId = g.Key.Id,
                g.Key.WarehouseId,
                First = g.First(),
                Quantity = g.Sum(x => x.QuantityRemaining),
            })
            .ToList();

        var productIds = grouped.Select(x => x.First.ProductId).Distinct().ToList();

        var products = await db.Products
            .Where(x => x.OrganizationId == request.OrganizationId && productIds.Contains(x.Id))
            .Join(db.UnitsOfMeasurement.Where(u => u.OrganizationId == request.OrganizationId),
                p => p.PrimaryUnitId, u => u.Id,
                (p, u) => new { p.Id, p.Code, p.Name, UnitName = u.Name })
            .ToDictionaryAsync(x => x.Id, cancellationToken);

        var warehouseIds = grouped.Where(x => x.WarehouseId is not null)
            .Select(x => x.WarehouseId!.Value).Distinct().ToList();

        var warehouses = await db.Warehouses
            .Where(x => x.OrganizationId == request.OrganizationId && warehouseIds.Contains(x.Id))
            .ToDictionaryAsync(x => x.Id, x => x.Name, cancellationToken);

        var items = grouped
            .Select(x =>
            {
                var product = products.GetValueOrDefault(x.First.ProductId);
                return new ProductBatchRowDto(
                    x.BatchId,
                    x.First.BatchNo,
                    x.First.ProductId,
                    product?.Code ?? string.Empty,
                    product?.Name ?? string.Empty,
                    x.First.ManufactureDate,
                    x.First.ExpiryDate,
                    x.WarehouseId,
                    x.WarehouseId is null ? null : warehouses.GetValueOrDefault(x.WarehouseId.Value),
                    x.Quantity,
                    product?.UnitName ?? string.Empty);
            })
            .OrderBy(x => request.GroupBy == ProductBatchGroupBy.Product ? x.ProductName : string.Empty, StringComparer.Ordinal)
            .ThenBy(x => x.ExpiryDate ?? DateOnly.MaxValue)
            .ThenBy(x => x.BatchNo, StringComparer.Ordinal)
            .ThenBy(x => x.WarehouseName ?? string.Empty, StringComparer.Ordinal)
            .ToList();

        // The footer total is over the whole filtered set, computed before paging -- phase-16c
        // bug #1. Taken here rather than from the page for exactly that reason.
        var totalQuantity = items.Sum(x => x.Quantity);
        var totalCount = items.Count;

        var page = request.ExportAll
            ? items
            : items.Skip((request.Page - 1) * request.PageSize).Take(request.PageSize).ToList();

        return new ProductBatchReportDto(
            request.FromDate, request.ToDate, page,
            request.ExportAll ? 1 : request.Page,
            request.ExportAll ? totalCount : request.PageSize,
            totalCount, totalQuantity);
    }
}
