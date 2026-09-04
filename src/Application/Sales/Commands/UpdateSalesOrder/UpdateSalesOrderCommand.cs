using ErpApp.Application.Common.Security;
using ErpApp.Domain.Common;
using ErpApp.Domain.Sales;
using MediatR;

namespace ErpApp.Application.Sales.Commands.UpdateSalesOrder;

public sealed record UpdateSalesOrderCommand(
    Guid OrganizationId, Guid Id, Guid ContactId, DateOnly Date, DateOnly? DeliveryDate, string? Reference,
    IReadOnlyList<SalesOrderLineInput> Lines, decimal DiscountPct = 0,
    // Phase 27b -- the "+ Add Terms and Conditions" block's text, pre-filled client-side from a
    // CustomTemplate and editable from there. Optional and trailing so no existing caller changes.
    string? Terms = null)
    : IRequest<UpdateSalesOrderResult>, IRequirePermission, IOrganizationScoped, ILockDateSensitive, IAuditableRequestWithId
{
    public string PermissionKey => PermissionKeys.SalesOrderEdit;
    public DocumentType AuditDocumentType => DocumentType.SalesOrder;
    public Guid AuditDocumentId => Id;
}

public sealed record UpdateSalesOrderResult(Guid Id, string Code, SalesOrderStatus Status);
