using ErpApp.Application.Common.Security;
using ErpApp.Domain.Common;
using ErpApp.Domain.Inventory;
using MediatR;

namespace ErpApp.Application.Inventory.Commands.VoidWarehouseTransfer;

public sealed record VoidWarehouseTransferCommand(Guid OrganizationId, Guid Id)
    : IRequest<VoidWarehouseTransferResult>, IRequirePermission, IOrganizationScoped, ILockDateSensitiveDocument
{
    public string PermissionKey => PermissionKeys.WarehouseTransferVoid;
    public DocumentType LockDateDocumentType => DocumentType.WarehouseTransfer;
    public Guid LockDateDocumentId => Id;
}

public sealed record VoidWarehouseTransferResult(Guid Id, string Code, WarehouseTransferStatus Status, DateTimeOffset? VoidedAt);
