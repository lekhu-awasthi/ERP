using ErpApp.Application.Common.Security;
using ErpApp.Domain.Common;
using ErpApp.Domain.Sales;
using MediatR;

namespace ErpApp.Application.Sales.Commands.CreateSalesOrder;

public sealed record CreateSalesOrderCommand(
    Guid OrganizationId, Guid ContactId, DateOnly Date, DateOnly? DeliveryDate, string? Reference,
    IReadOnlyList<SalesOrderLineInput> Lines, decimal DiscountPct = 0)
    : IRequest<CreateSalesOrderResult>, IRequirePermission, IOrganizationScoped, ILockDateSensitive, IAuditableRequest
{
    public string PermissionKey => PermissionKeys.SalesOrderCreate;
    public DocumentType AuditDocumentType => DocumentType.SalesOrder;
}

public sealed record CreateSalesOrderResult(Guid Id, string Code, SalesOrderStatus Status);
