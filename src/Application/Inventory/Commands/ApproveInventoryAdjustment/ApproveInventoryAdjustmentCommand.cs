using ErpApp.Application.Common.Security;
using ErpApp.Domain.Common;
using ErpApp.Domain.Inventory;
using MediatR;

namespace ErpApp.Application.Inventory.Commands.ApproveInventoryAdjustment;

public sealed record ApproveInventoryAdjustmentCommand(Guid OrganizationId, Guid Id)
    : IRequest<ApproveInventoryAdjustmentResult>, IRequirePermission, IOrganizationScoped, ILockDateSensitiveDocument
{
    public string PermissionKey => PermissionKeys.InventoryAdjustmentApprove;
    public DocumentType LockDateDocumentType => DocumentType.InventoryAdjustment;
    public Guid LockDateDocumentId => Id;
}

public sealed record ApproveInventoryAdjustmentResult(
    Guid Id, string Code, InventoryAdjustmentStatus Status, DateTimeOffset? ApprovedAt);
