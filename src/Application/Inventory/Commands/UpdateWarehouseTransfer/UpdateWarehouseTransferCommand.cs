using ErpApp.Application.Common.Security;
using ErpApp.Domain.Common;
using ErpApp.Domain.Inventory;
using MediatR;

namespace ErpApp.Application.Inventory.Commands.UpdateWarehouseTransfer;

public sealed record UpdateWarehouseTransferCommand(
    Guid OrganizationId,
    Guid Id,
    Guid FromWarehouseId,
    Guid ToWarehouseId,
    DateOnly Date,
    string? Reference,
    IReadOnlyList<WarehouseTransferLineInput> Lines)
    : IRequest<UpdateWarehouseTransferResult>, IRequirePermission, IOrganizationScoped, ILockDateSensitive, IAuditableRequestWithId
{
    public string PermissionKey => PermissionKeys.WarehouseTransferEdit;
    public DocumentType AuditDocumentType => DocumentType.WarehouseTransfer;
    public Guid AuditDocumentId => Id;
}

public sealed record UpdateWarehouseTransferResult(Guid Id, string Code, WarehouseTransferStatus Status);
