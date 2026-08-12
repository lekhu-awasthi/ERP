using ErpApp.Application.Common.Security;
using ErpApp.Domain.Sales;
using MediatR;

namespace ErpApp.Application.Sales.Commands.ApproveSalesOrder;

public sealed record ApproveSalesOrderCommand(Guid OrganizationId, Guid Id)
    : IRequest<ApproveSalesOrderResult>, IRequirePermission, IOrganizationScoped
{
    public string PermissionKey => PermissionKeys.SalesOrderApprove;
}

public sealed record ApproveSalesOrderResult(Guid Id, string Code, SalesOrderStatus Status, DateTimeOffset? ApprovedAt);
