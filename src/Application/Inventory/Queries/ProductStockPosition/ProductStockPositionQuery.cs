using ErpApp.Application.Common.Security;
using ErpApp.Domain.Tenancy;
using MediatR;

namespace ErpApp.Application.Inventory.Queries.ProductStockPosition;

/// <summary>architecture-spec.md §4.3's ProductStockPositionQuery -- Opening/In/Out/Balance per
/// (Product, Warehouse), optionally filtered to one Product and/or one Warehouse. Opening is
/// always 0 -- there's no OpeningStockLine/day-zero inventory concept built yet (architecture-
/// spec.md §4.7 reserves it, not in scope this phase); In/Out/Balance are derived entirely from
/// StockLedgerEntry (In = every layer's original QuantityIn ever created here, Balance = what
/// remains, Out = the difference), so they're accurate regardless.</summary>
public sealed record ProductStockPositionQuery(Guid OrganizationId, Guid? ProductId, Guid? WarehouseId)
    : IRequest<IReadOnlyList<StockPositionDto>>, IRequirePermission, IOrganizationScoped, IRequireFeature
{
    public string PermissionKey => PermissionKeys.InventoryLedgerView;

    // Phase 20f (FR-2.6): the Inventory context is only available to a tenant that opted
    // into Track Inventory. Catalog (Products/Categories/Units) is deliberately NOT gated --
    // live-confirmed that the reference product files those under Inventory in its nav but
    // every tenant needs them. See phase-20f-status.md.
    public IReadOnlyCollection<TenantFeature> RequiredFeatures => [TenantFeature.TrackInventory];
}

public sealed record StockPositionDto(Guid ProductId, Guid WarehouseId, decimal Opening, decimal In, decimal Out, decimal Balance);
