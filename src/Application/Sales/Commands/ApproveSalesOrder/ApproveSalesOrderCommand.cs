using ErpApp.Application.Common.Security;
using ErpApp.Domain.Common;
using ErpApp.Domain.Sales;
using MediatR;

namespace ErpApp.Application.Sales.Commands.ApproveSalesOrder;

public sealed record ApproveSalesOrderCommand(Guid OrganizationId, Guid Id)
    : IRequest<ApproveSalesOrderResult>, IRequirePermission, IOrganizationScoped, ILockDateSensitiveDocument
{
    public string PermissionKey => PermissionKeys.SalesOrderApprove;
    public DocumentType LockDateDocumentType => DocumentType.SalesOrder;
    public Guid LockDateDocumentId => Id;
}

public sealed record ApproveSalesOrderResult(Guid Id, string Code, SalesOrderStatus Status, DateTimeOffset? ApprovedAt);
