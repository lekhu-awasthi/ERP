using ErpApp.Application.Common.Security;
using ErpApp.Domain.Inventory;
using MediatR;

namespace ErpApp.Application.Inventory.Commands.ApproveInventoryAdjustment;

public sealed record ApproveInventoryAdjustmentCommand(Guid OrganizationId, Guid Id)
    : IRequest<ApproveInventoryAdjustmentResult>, IRequirePermission, IOrganizationScoped
{
    public string PermissionKey => PermissionKeys.InventoryAdjustmentApprove;
}

public sealed record ApproveInventoryAdjustmentResult(
    Guid Id, string Code, InventoryAdjustmentStatus Status, DateTimeOffset? ApprovedAt);
