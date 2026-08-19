using ErpApp.Application.Common.Security;
using ErpApp.Domain.Common;
using ErpApp.Domain.Sales;
using MediatR;

namespace ErpApp.Application.Sales.Commands.VoidSalesOrder;

public sealed record VoidSalesOrderCommand(Guid OrganizationId, Guid Id)
    : IRequest<VoidSalesOrderResult>, IRequirePermission, IOrganizationScoped, ILockDateSensitiveDocument
{
    public string PermissionKey => PermissionKeys.SalesOrderVoid;
    public DocumentType LockDateDocumentType => DocumentType.SalesOrder;
    public Guid LockDateDocumentId => Id;
}

public sealed record VoidSalesOrderResult(Guid Id, string Code, SalesOrderStatus Status, DateTimeOffset? VoidedAt);
