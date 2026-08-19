using ErpApp.Application.Common.Pagination;
using ErpApp.Application.Common.Security;
using ErpApp.Domain.Inventory;
using MediatR;

namespace ErpApp.Application.Inventory.Queries.ListInventoryAdjustments;

public sealed record ListInventoryAdjustmentsQuery(
    Guid OrganizationId,
    InventoryAdjustmentStatus? Status,
    int Page = 1,
    int PageSize = PagingDefaults.DefaultPageSize)
    : IRequest<PagedResult<InventoryAdjustment>>, IRequirePermission, IOrganizationScoped
{
    public string PermissionKey => PermissionKeys.InventoryAdjustmentView;
}
