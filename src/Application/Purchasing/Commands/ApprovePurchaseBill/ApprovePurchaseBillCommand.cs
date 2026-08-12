using ErpApp.Application.Common.Security;
using ErpApp.Domain.Purchasing;
using MediatR;

namespace ErpApp.Application.Purchasing.Commands.ApprovePurchaseBill;

public sealed record ApprovePurchaseBillCommand(Guid OrganizationId, Guid Id)
    : IRequest<ApprovePurchaseBillResult>, IRequirePermission, IOrganizationScoped
{
    public string PermissionKey => PermissionKeys.PurchaseBillApprove;
}

public sealed record ApprovePurchaseBillResult(Guid Id, string Code, PurchaseBillStatus Status, DateTimeOffset? ApprovedAt);
