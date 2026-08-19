using ErpApp.Application.Common.Security;
using ErpApp.Domain.Common;
using ErpApp.Domain.Inventory;
using MediatR;

namespace ErpApp.Application.Inventory.Commands.VoidInventoryAdjustment;

public sealed record VoidInventoryAdjustmentCommand(Guid OrganizationId, Guid Id)
    : IRequest<VoidInventoryAdjustmentResult>, IRequirePermission, IOrganizationScoped, ILockDateSensitiveDocument
{
    public string PermissionKey => PermissionKeys.InventoryAdjustmentVoid;
    public DocumentType LockDateDocumentType => DocumentType.InventoryAdjustment;
    public Guid LockDateDocumentId => Id;
}

public sealed record VoidInventoryAdjustmentResult(
    Guid Id, string Code, InventoryAdjustmentStatus Status, DateTimeOffset? VoidedAt);
