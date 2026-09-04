using ErpApp.Application.Common.Security;
using ErpApp.Domain.Common;
using ErpApp.Domain.Purchasing;
using MediatR;

namespace ErpApp.Application.Purchasing.Commands.UpdatePurchaseOrder;

public sealed record UpdatePurchaseOrderCommand(
    Guid OrganizationId, Guid Id, Guid ContactId, DateOnly Date, string? Reference,
    IReadOnlyList<PurchaseOrderLineInput> Lines, decimal DiscountPct = 0,
    // Phase 27b -- the "+ Add Terms and Conditions" block's text, pre-filled client-side from a
    // CustomTemplate and editable from there. Optional and trailing so no existing caller changes.
    string? Terms = null)
    : IRequest<UpdatePurchaseOrderResult>, IRequirePermission, IOrganizationScoped, ILockDateSensitive, IAuditableRequestWithId
{
    public string PermissionKey => PermissionKeys.PurchaseOrderEdit;
    public DocumentType AuditDocumentType => DocumentType.PurchaseOrder;
    public Guid AuditDocumentId => Id;
}

public sealed record UpdatePurchaseOrderResult(Guid Id, string Code, PurchaseOrderStatus Status);
