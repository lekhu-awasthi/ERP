using ErpApp.Application.Common.Security;
using ErpApp.Domain.Common;
using ErpApp.Domain.Sales;
using MediatR;

namespace ErpApp.Application.Sales.Commands.CreateSalesOrder;

public sealed record CreateSalesOrderCommand(
    Guid OrganizationId, Guid ContactId, DateOnly Date, DateOnly? DeliveryDate, string? Reference,
    IReadOnlyList<SalesOrderLineInput> Lines, decimal DiscountPct = 0,
    // Phase 27b -- the "+ Add Terms and Conditions" block's text, pre-filled client-side from a
    // CustomTemplate and editable from there. Optional and trailing so no existing caller changes.
    string? Terms = null)
    : IRequest<CreateSalesOrderResult>, IRequirePermission, IOrganizationScoped, ILockDateSensitive, IAuditableRequest
{
    public string PermissionKey => PermissionKeys.SalesOrderCreate;
    public DocumentType AuditDocumentType => DocumentType.SalesOrder;
}

public sealed record CreateSalesOrderResult(Guid Id, string Code, SalesOrderStatus Status);
