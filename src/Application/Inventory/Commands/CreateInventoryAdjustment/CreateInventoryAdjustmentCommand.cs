using ErpApp.Application.Common.Security;
using ErpApp.Domain.Common;
using ErpApp.Domain.Inventory;
using MediatR;

namespace ErpApp.Application.Inventory.Commands.CreateInventoryAdjustment;

public sealed record CreateInventoryAdjustmentCommand(
    Guid OrganizationId,
    Guid WarehouseId,
    DateOnly Date,
    string? Reference,
    IReadOnlyList<InventoryAdjustmentLineInput> Lines)
    : IRequest<CreateInventoryAdjustmentResult>, IRequirePermission, IOrganizationScoped, ILockDateSensitive, IAuditableRequest
{
    public string PermissionKey => PermissionKeys.InventoryAdjustmentCreate;
    public DocumentType AuditDocumentType => DocumentType.InventoryAdjustment;
}

public sealed record CreateInventoryAdjustmentResult(Guid Id, string Code, InventoryAdjustmentStatus Status);
