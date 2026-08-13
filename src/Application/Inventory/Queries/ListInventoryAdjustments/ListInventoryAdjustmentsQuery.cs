using ErpApp.Application.Common.Security;
using ErpApp.Domain.Inventory;
using MediatR;

namespace ErpApp.Application.Inventory.Queries.ListInventoryAdjustments;

public sealed record ListInventoryAdjustmentsQuery(Guid OrganizationId, InventoryAdjustmentStatus? Status)
    : IRequest<IReadOnlyList<InventoryAdjustment>>, IRequirePermission, IOrganizationScoped
{
    public string PermissionKey => PermissionKeys.InventoryAdjustmentView;
}
