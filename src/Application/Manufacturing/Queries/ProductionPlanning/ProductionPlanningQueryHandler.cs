using ErpApp.Application.Common.Exceptions;
using ErpApp.Application.Common.Persistence;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.Manufacturing.Queries.ProductionPlanning;

public sealed class ProductionPlanningQueryHandler(IAppDbContext db)
    : IRequestHandler<ProductionPlanningQuery, ProductionPlanningReportDto>
{
    public async Task<ProductionPlanningReportDto> Handle(
        ProductionPlanningQuery request, CancellationToken cancellationToken)
    {
        var product = await db.Products
            .Where(x => x.Id == request.ProductId && x.OrganizationId == request.OrganizationId)
            .Select(x => new { x.Name })
            .SingleOrDefaultAsync(cancellationToken)
            ?? throw new NotFoundException("Product not found.");

        var bom = await db.BillsOfMaterials
            .Include(x => x.RawMaterials)
            .SingleOrDefaultAsync(
                x => x.OrganizationId == request.OrganizationId && x.ProductId == request.ProductId && x.IsActive,
                cancellationToken);

        // No recipe is an answer, not an error: the report renders the header and an empty table,
        // which is what "this product is not manufactured here" looks like.
        if (bom is null || bom.OutputQuantity <= 0)
        {
            return new ProductionPlanningReportDto(
                request.ProductId, product.Name, request.Quantity, null, null, MultipleLevel: false, []);
        }

        var scale = request.Quantity / bom.OutputQuantity;

        var required = bom.RawMaterials
            .GroupBy(x => x.ProductId)
            .ToDictionary(g => g.Key, g => g.Sum(x => x.Quantity) * scale);

        var rawProductIds = required.Keys.ToList();
        var labels = await ProductLabels.LoadAsync(db, request.OrganizationId, rawProductIds, cancellationToken);

        // One grouped read rather than a GetAvailableQuantityAsync call per material: this report
        // is a planning aid that may list dozens of inputs, and the ledger sum is the same either
        // way. Filtering by warehouse only when one was asked for gives the reference product's
        // all-warehouses figure by default.
        var availability = await db.StockLedgerEntries
            .Where(x => x.OrganizationId == request.OrganizationId
                && rawProductIds.Contains(x.ProductId)
                && (request.WarehouseId == null || x.WarehouseId == request.WarehouseId))
            .GroupBy(x => x.ProductId)
            .Select(g => new { ProductId = g.Key, Quantity = g.Sum(x => x.QuantityRemaining) })
            .ToDictionaryAsync(x => x.ProductId, x => x.Quantity, cancellationToken);

        var lines = required
            .Select(entry =>
            {
                var label = labels.GetValueOrDefault(entry.Key);
                var available = availability.GetValueOrDefault(entry.Key);
                return new ProductionPlanningLineDto(
                    entry.Key,
                    label?.Name ?? string.Empty,
                    label?.Code ?? string.Empty,
                    label?.UnitName,
                    entry.Value,
                    available,
                    available - entry.Value);
            })
            .OrderBy(x => x.ProductName, StringComparer.Ordinal)
            .ToList();

        return new ProductionPlanningReportDto(
            request.ProductId, product.Name, request.Quantity, bom.Id, bom.OutputQuantity, MultipleLevel: false, lines);
    }
}
