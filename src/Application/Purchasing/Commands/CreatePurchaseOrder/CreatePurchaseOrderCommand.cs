using ErpApp.Application.Common.Security;
using ErpApp.Domain.Common;
using ErpApp.Domain.Purchasing;
using MediatR;

namespace ErpApp.Application.Purchasing.Commands.CreatePurchaseOrder;

public sealed record CreatePurchaseOrderCommand(
    Guid OrganizationId, Guid ContactId, DateOnly Date, string? Reference, IReadOnlyList<PurchaseOrderLineInput> Lines,
    decimal DiscountPct = 0)
    : IRequest<CreatePurchaseOrderResult>, IRequirePermission, IOrganizationScoped, ILockDateSensitive, IAuditableRequest
{
    public string PermissionKey => PermissionKeys.PurchaseOrderCreate;
    public DocumentType AuditDocumentType => DocumentType.PurchaseOrder;
}

public sealed record CreatePurchaseOrderResult(Guid Id, string Code, PurchaseOrderStatus Status);
