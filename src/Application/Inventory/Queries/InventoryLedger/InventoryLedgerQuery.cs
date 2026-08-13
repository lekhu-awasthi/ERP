using ErpApp.Application.Common.Security;
using ErpApp.Domain.Common;
using ErpApp.Domain.Inventory;
using MediatR;

namespace ErpApp.Application.Inventory.Queries.InventoryLedger;

/// <summary>architecture-spec.md §4.3's InventoryLedgerQuery -- the kardex view, chronological
/// StockMovement rows (see that entity's doc comment for why StockLedgerEntry alone can't produce
/// this) for one Product+Warehouse with a running balance computed in the same pass.</summary>
public sealed record InventoryLedgerQuery(Guid OrganizationId, Guid ProductId, Guid WarehouseId)
    : IRequest<IReadOnlyList<InventoryLedgerRowDto>>, IRequirePermission, IOrganizationScoped
{
    public string PermissionKey => PermissionKeys.InventoryLedgerView;
}

public sealed record InventoryLedgerRowDto(
    Guid Id,
    DateOnly TransactionDate,
    DocumentType SourceDocumentType,
    Guid SourceDocumentId,
    StockMovementDirection Direction,
    decimal Quantity,
    decimal UnitCost,
    decimal RunningBalance);
