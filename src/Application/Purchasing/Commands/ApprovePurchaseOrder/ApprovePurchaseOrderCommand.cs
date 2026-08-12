using ErpApp.Application.Common.Security;
using ErpApp.Domain.Purchasing;
using MediatR;

namespace ErpApp.Application.Purchasing.Commands.ApprovePurchaseOrder;

public sealed record ApprovePurchaseOrderCommand(Guid OrganizationId, Guid Id)
    : IRequest<ApprovePurchaseOrderResult>, IRequirePermission, IOrganizationScoped
{
    public string PermissionKey => PermissionKeys.PurchaseOrderApprove;
}

public sealed record ApprovePurchaseOrderResult(Guid Id, string Code, PurchaseOrderStatus Status, DateTimeOffset? ApprovedAt);
