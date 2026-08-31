using ErpApp.Application.Common.Security;
using ErpApp.Domain.Common;
using ErpApp.Domain.Inventory;
using ErpApp.Domain.Tenancy;
using MediatR;

namespace ErpApp.Application.Inventory.Queries.InventoryLedger;

/// <summary>architecture-spec.md §4.3's InventoryLedgerQuery -- the kardex view, chronological
/// StockMovement rows (see that entity's doc comment for why StockLedgerEntry alone can't produce
/// this) for one Product+Warehouse with a running balance computed in the same pass.</summary>
public sealed record InventoryLedgerQuery(Guid OrganizationId, Guid ProductId, Guid WarehouseId)
    : IRequest<IReadOnlyList<InventoryLedgerRowDto>>, IRequirePermission, IOrganizationScoped, IRequireFeature
{
    public string PermissionKey => PermissionKeys.InventoryLedgerView;

    // Phase 20f (FR-2.6): the Inventory context is only available to a tenant that opted
    // into Track Inventory. Catalog (Products/Categories/Units) is deliberately NOT gated --
    // live-confirmed that the reference product files those under Inventory in its nav but
    // every tenant needs them. See phase-20f-status.md.
    public IReadOnlyCollection<TenantFeature> RequiredFeatures => [TenantFeature.TrackInventory];
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
