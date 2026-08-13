using ErpApp.Application.Common.Persistence;
using ErpApp.Domain.Inventory;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.Inventory.Queries.InventoryLedger;

public sealed class InventoryLedgerQueryHandler(IAppDbContext db)
    : IRequestHandler<InventoryLedgerQuery, IReadOnlyList<InventoryLedgerRowDto>>
{
    public async Task<IReadOnlyList<InventoryLedgerRowDto>> Handle(InventoryLedgerQuery request, CancellationToken cancellationToken)
    {
        var movements = await db.StockMovements
            .Where(x => x.OrganizationId == request.OrganizationId
                && x.ProductId == request.ProductId && x.WarehouseId == request.WarehouseId)
            .OrderBy(x => x.TransactionDate)
            .ThenBy(x => x.CreatedAt)
            .ToListAsync(cancellationToken);

        var rows = new List<InventoryLedgerRowDto>();
        var runningBalance = 0m;

        foreach (var movement in movements)
        {
            runningBalance += movement.Direction == StockMovementDirection.In ? movement.Quantity : -movement.Quantity;

            rows.Add(new InventoryLedgerRowDto(
                movement.Id,
                movement.TransactionDate,
                movement.SourceDocumentType,
                movement.SourceDocumentId,
                movement.Direction,
                movement.Quantity,
                movement.UnitCost,
                runningBalance));
        }

        return rows;
    }
}
