using ErpApp.Application.Common.Security;
using ErpApp.Domain.Common;
using ErpApp.Domain.Purchasing;
using MediatR;

namespace ErpApp.Application.Purchasing.Commands.UpdatePurchaseOrder;

public sealed record UpdatePurchaseOrderCommand(
    Guid OrganizationId, Guid Id, Guid ContactId, DateOnly Date, string? Reference,
    IReadOnlyList<PurchaseOrderLineInput> Lines, decimal DiscountPct = 0)
    : IRequest<UpdatePurchaseOrderResult>, IRequirePermission, IOrganizationScoped, ILockDateSensitive, IAuditableRequestWithId
{
    public string PermissionKey => PermissionKeys.PurchaseOrderEdit;
    public DocumentType AuditDocumentType => DocumentType.PurchaseOrder;
    public Guid AuditDocumentId => Id;
}

public sealed record UpdatePurchaseOrderResult(Guid Id, string Code, PurchaseOrderStatus Status);
