using ErpApp.Application.Common.Security;
using ErpApp.Domain.Inventory;
using MediatR;

namespace ErpApp.Application.Inventory.Commands.CreateInventoryAdjustment;

public sealed record CreateInventoryAdjustmentCommand(
    Guid OrganizationId,
    Guid WarehouseId,
    DateOnly Date,
    string? Reference,
    IReadOnlyList<InventoryAdjustmentLineInput> Lines)
    : IRequest<CreateInventoryAdjustmentResult>, IRequirePermission, IOrganizationScoped, ILockDateSensitive
{
    public string PermissionKey => PermissionKeys.InventoryAdjustmentCreate;
}

public sealed record CreateInventoryAdjustmentResult(Guid Id, string Code, InventoryAdjustmentStatus Status);
