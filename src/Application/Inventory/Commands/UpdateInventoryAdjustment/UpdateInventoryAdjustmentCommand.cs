using ErpApp.Application.Common.Security;
using ErpApp.Domain.Inventory;
using MediatR;

namespace ErpApp.Application.Inventory.Commands.UpdateInventoryAdjustment;

public sealed record UpdateInventoryAdjustmentCommand(
    Guid OrganizationId,
    Guid Id,
    Guid WarehouseId,
    DateOnly Date,
    string? Reference,
    IReadOnlyList<InventoryAdjustmentLineInput> Lines)
    : IRequest<UpdateInventoryAdjustmentResult>, IRequirePermission, IOrganizationScoped, ILockDateSensitive
{
    public string PermissionKey => PermissionKeys.InventoryAdjustmentEdit;
}

public sealed record UpdateInventoryAdjustmentResult(Guid Id, string Code, InventoryAdjustmentStatus Status);
