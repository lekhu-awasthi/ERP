using ErpApp.Application.Common.Security;
using MediatR;

namespace ErpApp.Application.Inventory.Commands.CreateOrUpdateOpeningStockLine;

/// <summary>Phase 17 (Configurations §18, docs/phase-17-status.md) -- sets (or corrects) one
/// Product's opening stock quantity/rate in one Warehouse. Posts a real FIFO layer via
/// IStockLedgerService.IncrementAsync so Stock Position needs no query change to see it.</summary>
public sealed record CreateOrUpdateOpeningStockLineCommand(
    Guid OrganizationId, Guid ProductId, Guid WarehouseId, decimal Quantity, decimal Rate)
    : IRequest<OpeningStockLineResult>, IRequirePermission, IOrganizationScoped
{
    public string PermissionKey => PermissionKeys.OpeningBalanceEdit;
}

public sealed record OpeningStockLineResult(Guid Id, Guid ProductId, Guid WarehouseId, decimal Quantity, decimal Rate);
