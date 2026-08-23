using ErpApp.Application.Common.Pagination;
using ErpApp.Application.Common.Persistence;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.Inventory.Queries.StockAgeing;

public sealed class StockAgeingQueryHandler(IAppDbContext db) : IRequestHandler<StockAgeingQuery, StockAgeingDto>
{
    public async Task<StockAgeingDto> Handle(StockAgeingQuery request, CancellationToken cancellationToken)
    {
        var query = db.StockLedgerEntries.Where(x =>
            x.OrganizationId == request.OrganizationId && x.TransactionDate <= request.AsOfDate);

        if (request.ProductId is { } productId)
        {
            query = query.Where(x => x.ProductId == productId);
        }
        if (request.WarehouseId is { } warehouseId)
        {
            query = query.Where(x => x.WarehouseId == warehouseId);
        }

        var entries = await query
            .Select(x => new { x.ProductId, x.TransactionDate, x.QuantityRemaining, x.UnitCost })
            .ToListAsync(cancellationToken);

        var productIds = entries.Select(x => x.ProductId).Distinct().ToList();
        var productsQuery = db.Products.Where(x => x.OrganizationId == request.OrganizationId && productIds.Contains(x.Id));
        if (request.ProductCategoryId is { } categoryId)
        {
            productsQuery = productsQuery.Where(x => x.CategoryId == categoryId);
        }
        var products = await productsQuery
            .Select(x => new { x.Id, x.Code, x.Name, x.CategoryId, x.PrimaryUnitId })
            .ToDictionaryAsync(x => x.Id, cancellationToken);

        var categoryIds = products.Values.Select(x => x.CategoryId).Distinct().ToList();
        var categoryNames = await db.ProductCategories
            .Where(x => categoryIds.Contains(x.Id))
            .Select(x => new { x.Id, x.Name })
            .ToDictionaryAsync(x => x.Id, x => x.Name, cancellationToken);

        var unitIds = products.Values.Select(x => x.PrimaryUnitId).Distinct().ToList();
        var unitShortNames = await db.UnitsOfMeasurement
            .Where(x => unitIds.Contains(x.Id))
            .Select(x => new { x.Id, x.ShortName })
            .ToDictionaryAsync(x => x.Id, x => x.ShortName, cancellationToken);

        var rows = new List<StockAgeingRowDto>();
        foreach (var group in entries.Where(e => products.ContainsKey(e.ProductId)).GroupBy(x => x.ProductId))
        {
            var product = products[group.Key];
            var buckets = new decimal[4];
            decimal totalValue = 0;

            foreach (var entry in group)
            {
                var age = request.AsOfDate.DayNumber - entry.TransactionDate.DayNumber;
                var bucketIndex = age <= 30 ? 0 : age <= 60 ? 1 : age <= 90 ? 2 : 3;
                buckets[bucketIndex] += entry.QuantityRemaining;
                totalValue += entry.QuantityRemaining * entry.UnitCost;
            }

            var total = buckets.Sum();
            rows.Add(new StockAgeingRowDto(
                product.Id, product.Code, product.Name,
                categoryNames.GetValueOrDefault(product.CategoryId, "—"),
                unitShortNames.GetValueOrDefault(product.PrimaryUnitId, "—"),
                buckets[0], buckets[1], buckets[2], buckets[3], total,
                Rate: total == 0 ? 0 : totalValue / total, Amount: totalValue));
        }

        var orderedRows = rows.OrderBy(x => x.ProductName).ToList();
        var paged = request.ExportAll ? orderedRows.ToUnpagedResult() : orderedRows.ToPagedResult(request.Page, request.PageSize);

        return new StockAgeingDto(
            request.AsOfDate, paged.Items, paged.Page, paged.PageSize, paged.TotalCount,
            orderedRows.Sum(x => x.Days1To30), orderedRows.Sum(x => x.Days31To60),
            orderedRows.Sum(x => x.Days61To90), orderedRows.Sum(x => x.Days91Plus),
            orderedRows.Sum(x => x.Amount));
    }
}
