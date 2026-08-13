using ErpApp.Application.Common.Security;
using ErpApp.Domain.Inventory;
using MediatR;

namespace ErpApp.Application.Inventory.Commands.ApproveWarehouseTransfer;

public sealed record ApproveWarehouseTransferCommand(Guid OrganizationId, Guid Id)
    : IRequest<ApproveWarehouseTransferResult>, IRequirePermission, IOrganizationScoped
{
    public string PermissionKey => PermissionKeys.WarehouseTransferApprove;
}

public sealed record ApproveWarehouseTransferResult(Guid Id, string Code, WarehouseTransferStatus Status, DateTimeOffset? ApprovedAt);
