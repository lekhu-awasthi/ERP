using ErpApp.Application.Common.Security;
using ErpApp.Domain.Tenancy;
using MediatR;

namespace ErpApp.Application.Inventory.Commands.CreateOrUpdateOpeningStockLine;

/// <summary>Phase 17 (Configurations §18, docs/phase-17-status.md) -- sets (or corrects) one
/// Product's opening stock quantity/rate in one Warehouse. Posts a real FIFO layer via
/// IStockLedgerService.IncrementAsync so Stock Position needs no query change to see it.</summary>
public sealed record CreateOrUpdateOpeningStockLineCommand(
    Guid OrganizationId, Guid ProductId, Guid WarehouseId, decimal Quantity, decimal Rate)
    : IRequest<OpeningStockLineResult>, IRequirePermission, IOrganizationScoped, IRequireFeature
{
    public string PermissionKey => PermissionKeys.OpeningBalanceEdit;

    // Phase 20f (FR-2.6): the Inventory context is only available to a tenant that opted
    // into Track Inventory. Catalog (Products/Categories/Units) is deliberately NOT gated --
    // live-confirmed that the reference product files those under Inventory in its nav but
    // every tenant needs them. See phase-20f-status.md.
    public IReadOnlyCollection<TenantFeature> RequiredFeatures => [TenantFeature.TrackInventory];
}

public sealed record OpeningStockLineResult(Guid Id, Guid ProductId, Guid WarehouseId, decimal Quantity, decimal Rate);
