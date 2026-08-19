using ErpApp.Application.Common.Pagination;
using ErpApp.Application.Common.Security;
using ErpApp.Domain.Inventory;
using MediatR;

namespace ErpApp.Application.Inventory.Queries.ListWarehouseTransfers;

public sealed record ListWarehouseTransfersQuery(
    Guid OrganizationId,
    WarehouseTransferStatus? Status,
    int Page = 1,
    int PageSize = PagingDefaults.DefaultPageSize)
    : IRequest<PagedResult<WarehouseTransfer>>, IRequirePermission, IOrganizationScoped
{
    public string PermissionKey => PermissionKeys.WarehouseTransferView;
}
