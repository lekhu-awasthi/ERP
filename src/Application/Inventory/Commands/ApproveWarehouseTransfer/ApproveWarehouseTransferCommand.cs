using ErpApp.Application.Common.Security;
using ErpApp.Domain.Common;
using ErpApp.Domain.Inventory;
using MediatR;

namespace ErpApp.Application.Inventory.Commands.ApproveWarehouseTransfer;

public sealed record ApproveWarehouseTransferCommand(Guid OrganizationId, Guid Id)
    : IRequest<ApproveWarehouseTransferResult>, IRequirePermission, IOrganizationScoped, ILockDateSensitiveDocument
{
    public string PermissionKey => PermissionKeys.WarehouseTransferApprove;
    public DocumentType LockDateDocumentType => DocumentType.WarehouseTransfer;
    public Guid LockDateDocumentId => Id;
}

public sealed record ApproveWarehouseTransferResult(Guid Id, string Code, WarehouseTransferStatus Status, DateTimeOffset? ApprovedAt);
