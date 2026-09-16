using ErpApp.Application.Common.Locations;
using ErpApp.Application.Common.Persistence;
using ErpApp.Application.Common.Security;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.Inventory.Queries.ProductSerialReport;

/// <summary>
/// See the query's doc comment for what was read and what was derived.
///
/// <para>One row per serialised layer, which is one row per physical unit -- the model makes the
/// report's grain and the tab's grain the same thing, so there is no grouping to do and no second
/// quantity to reconcile. <c>Status</c> is read off <c>QuantityRemaining</c>.</para>
/// </summary>
public sealed class ProductSerialReportQueryHandler(IAppDbContext db, ICurrentUserService currentUser)
    : IRequestHandler<ProductSerialReportQuery, ProductSerialReportDto>
{
    public async Task<ProductSerialReportDto> Handle(
        ProductSerialReportQuery request, CancellationToken cancellationToken)
    {
        var allowedLocations = await LocationAccessScope.ForReportsAsync(
            db, currentUser, request.OrganizationId, cancellationToken);

        var layers = db.StockLedgerEntries
            .Where(x => x.OrganizationId == request.OrganizationId && x.SerialNo != null
                && x.TransactionDate >= request.FromDate && x.TransactionDate <= request.ToDate);

        // Composed, never folded -- an expression tree does not short-circuit (CLAUDE.md).
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

        // The Status filter, straight off the ledger column. A serialised layer is quantity one, so
        // "still has its quantity" and "still in stock" are the same predicate.
        layers = request.Status switch
        {
            ProductSerialStatusFilter.InStock => layers.Where(x => x.QuantityRemaining > 0),
            ProductSerialStatusFilter.Issued => layers.Where(x => x.QuantityRemaining <= 0),
            _ => layers,
        };

        var rows = await layers
            .Select(x => new
            {
                x.SerialNo,
                x.ProductId,
                x.WarehouseId,
                x.QuantityRemaining,
                x.CreatedAt,
                x.UnitCost,
            })
            .ToListAsync(cancellationToken);

        var productIds = rows.Select(x => x.ProductId).Distinct().ToList();
        var products = await db.Products
            .Where(x => x.OrganizationId == request.OrganizationId && productIds.Contains(x.Id))
            .Select(x => new { x.Id, x.Code, x.Name })
            .ToDictionaryAsync(x => x.Id, cancellationToken);

        var warehouseIds = rows.Select(x => x.WarehouseId).Distinct().ToList();
        var warehouses = await db.Warehouses
            .Where(x => x.OrganizationId == request.OrganizationId && warehouseIds.Contains(x.Id))
            .ToDictionaryAsync(x => x.Id, x => x.Name, cancellationToken);

        var items = rows
            .Select(x =>
            {
                var product = products.GetValueOrDefault(x.ProductId);
                return new ProductSerialRowDto(
                    x.SerialNo!,
                    x.ProductId,
                    product?.Code ?? string.Empty,
                    product?.Name ?? string.Empty,
                    x.WarehouseId,
                    warehouses.GetValueOrDefault(x.WarehouseId) ?? string.Empty,
                    x.QuantityRemaining > 0 ? ProductSerialStatus.InStock : ProductSerialStatus.Issued,
                    x.CreatedAt,
                    x.UnitCost);
            })
            .OrderBy(x => request.GroupBy == ProductSerialGroupBy.Product ? x.ProductName : string.Empty, StringComparer.Ordinal)
            .ThenBy(x => request.GroupBy == ProductSerialGroupBy.Warehouse ? x.WarehouseName : string.Empty, StringComparer.Ordinal)
            .ThenBy(x => x.SerialNo, StringComparer.Ordinal)
            .ToList();

        // Over the full filtered set, before paging -- phase-16c bug #1.
        var inStock = items.Count(x => x.Status == ProductSerialStatus.InStock);
        var issued = items.Count - inStock;

        var page = request.ExportAll
            ? items
            : items.Skip((request.Page - 1) * request.PageSize).Take(request.PageSize).ToList();

        return new ProductSerialReportDto(
            request.FromDate, request.ToDate, page,
            request.ExportAll ? 1 : request.Page,
            request.ExportAll ? items.Count : request.PageSize,
            items.Count, inStock, issued);
    }
}
