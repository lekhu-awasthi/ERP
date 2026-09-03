using ErpApp.Application.Common.Pagination;
using ErpApp.Application.Common.Persistence;
using ErpApp.Application.Inventory.Reports;
using MediatR;

namespace ErpApp.Application.Inventory.Queries.InventoryMovementReport;

public sealed class InventoryMovementReportQueryHandler(IAppDbContext db)
    : IRequestHandler<InventoryMovementReportQuery, InventoryMovementReportDto>
{
    public async Task<InventoryMovementReportDto> Handle(
        InventoryMovementReportQuery request, CancellationToken cancellationToken)
    {
        var products = await InventoryReportProducts.LoadAsync(
            db, request.OrganizationId, request.CategoryId, request.ProductId, cancellationToken);

        var movements = await StockFactReader.LoadMovementsAsync(
            db, request.OrganizationId, products.MatchingIds, request.WarehouseId, request.ToDate, cancellationToken);

        var rows = StockFactReader.Summarise(movements, request.FromDate)
            .Select(facts =>
            {
                var product = products.For(facts.ProductId);
                return new InventoryMovementRowDto(
                    facts.ProductId,
                    product?.Display ?? string.Empty,
                    product?.CategoryName ?? string.Empty,
                    Measure(facts.OpeningQuantity, facts.OpeningValue),
                    Measure(facts.InQuantity, facts.InValue),
                    Measure(facts.OutQuantity, facts.OutValue),
                    Measure(facts.BalanceQuantity, facts.BalanceValue));
            })
            .OrderBy(row => row.Product, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var paged = request.ExportAll ? rows.ToUnpagedResult() : rows.ToPagedResult(request.Page, request.PageSize);

        // Only the value columns are totalled. Quantities across products measured in kilograms,
        // pieces and litres do not add to anything a reader should believe -- the same judgment
        // phase-26a made about the Transaction list, applied one column at a time rather than to
        // the whole footer. (Inventory Position does total its single Qty column, because the live
        // report does; there, at least, the reader can see the units it is summing across.)
        return new InventoryMovementReportDto(
            request.FromDate, request.ToDate, paged.Items, paged.Page, paged.PageSize, paged.TotalCount,
            rows.Sum(row => row.Opening.Value),
            rows.Sum(row => row.In.Value),
            rows.Sum(row => row.Out.Value),
            rows.Sum(row => row.Balance.Value));
    }

    private static InventoryMovementMeasureDto Measure(decimal quantity, decimal value) =>
        new(quantity, StockFactReader.Rate(value, quantity), value);
}
