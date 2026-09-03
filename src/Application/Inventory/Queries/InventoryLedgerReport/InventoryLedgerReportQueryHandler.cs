using ErpApp.Application.Common.Pagination;
using ErpApp.Application.Common.Persistence;
using ErpApp.Application.Inventory.Reports;
using ErpApp.Domain.Inventory;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.Inventory.Queries.InventoryLedgerReport;

public sealed class InventoryLedgerReportQueryHandler(IAppDbContext db)
    : IRequestHandler<InventoryLedgerReportQuery, InventoryLedgerReportDto>
{
    public async Task<InventoryLedgerReportDto> Handle(
        InventoryLedgerReportQuery request, CancellationToken cancellationToken)
    {
        var products = await InventoryReportProducts.LoadAsync(
            db, request.OrganizationId, categoryId: null, request.ProductId, cancellationToken);
        var product = products.For(request.ProductId);

        var movements = await StockFactReader.LoadMovementsAsync(
            db, request.OrganizationId, [request.ProductId], request.WarehouseId, request.ToDate, cancellationToken);

        var facts = StockFactReader.Summarise(request.ProductId, movements, request.FromDate);

        var warehouses = await db.Warehouses
            .Where(w => w.OrganizationId == request.OrganizationId)
            .Select(w => new { w.Id, w.Name })
            .ToDictionaryAsync(w => w.Id, w => w.Name, cancellationToken);

        var periodMovements = movements.Where(m => m.TransactionDate >= request.FromDate).ToList();

        var resolver = await StockSourceDocumentResolver.LoadAsync(
            db,
            request.OrganizationId,
            [.. periodMovements.Select(m => (m.SourceDocumentType, m.SourceDocumentId))],
            cancellationToken);

        // The running balance is carried forward from the opening position, in the same
        // oldest-first order LoadMovementsAsync guarantees -- so each row's Balance is the position
        // *after* that movement, which is what a kardex means and what the live report shows.
        var runningQuantity = facts.OpeningQuantity;
        var runningValue = facts.OpeningValue;

        var rows = new List<InventoryLedgerReportRowDto>(periodMovements.Count);
        foreach (var movement in periodMovements)
        {
            var isIn = movement.Direction == StockMovementDirection.In;
            runningQuantity += isIn ? movement.Quantity : -movement.Quantity;
            runningValue += isIn ? movement.Value : -movement.Value;

            var document = resolver.For(movement.SourceDocumentType, movement.SourceDocumentId);
            var balanceValue = runningQuantity <= 0 ? 0 : runningValue;

            rows.Add(new InventoryLedgerReportRowDto(
                movement.Id,
                movement.TransactionDate,
                movement.SourceDocumentType,
                movement.SourceDocumentId,
                document?.Code ?? string.Empty,
                document?.Reference,
                document?.ContactName,
                warehouses.GetValueOrDefault(movement.WarehouseId, string.Empty),
                movement.Direction,
                isIn ? movement.Quantity : 0,
                isIn ? movement.UnitCost : 0,
                isIn ? movement.Value : 0,
                isIn ? 0 : movement.Quantity,
                isIn ? 0 : movement.UnitCost,
                isIn ? 0 : movement.Value,
                runningQuantity,
                StockFactReader.Rate(balanceValue, runningQuantity),
                balanceValue));
        }

        // Newest-first on the page, oldest-first for the running balance: the balance had to be
        // accumulated in date order above, and the live report lists movements the way every other
        // register in this codebase does.
        var ordered = rows.AsEnumerable().Reverse().ToList();
        var paged = request.ExportAll ? ordered.ToUnpagedResult() : ordered.ToPagedResult(request.Page, request.PageSize);

        return new InventoryLedgerReportDto(
            request.FromDate,
            request.ToDate,
            request.ProductId,
            product?.Display ?? string.Empty,
            facts.OpeningQuantity,
            StockFactReader.Rate(facts.OpeningQuantity <= 0 ? 0 : facts.OpeningValue, facts.OpeningQuantity),
            facts.OpeningQuantity <= 0 ? 0 : facts.OpeningValue,
            facts.BalanceQuantity,
            StockFactReader.Rate(facts.BalanceValue, facts.BalanceQuantity),
            facts.BalanceValue,
            paged.Items,
            paged.Page,
            paged.PageSize,
            paged.TotalCount);
    }
}
