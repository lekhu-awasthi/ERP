using ErpApp.Application.Common.Pagination;
using ErpApp.Application.Common.Security;
using ErpApp.Domain.Inventory;
using ErpApp.Domain.Tenancy;
using MediatR;

namespace ErpApp.Application.Inventory.Queries.ListWarehouseTransfers;

public sealed record ListWarehouseTransfersQuery(
    Guid OrganizationId,
    WarehouseTransferStatus? Status,
    int Page = 1,
    int PageSize = PagingDefaults.DefaultPageSize)
    : IRequest<PagedResult<WarehouseTransfer>>, IRequirePermission, IOrganizationScoped, IRequireFeature
{
    public string PermissionKey => PermissionKeys.WarehouseTransferView;

    // Phase 20f (FR-2.6): moving stock between warehouses needs both entitlements -- the
    // inventory tracking that gives the movement meaning, and more than one warehouse to
    // move it between. The only requests in this codebase requiring two features.
    public IReadOnlyCollection<TenantFeature> RequiredFeatures => [TenantFeature.TrackInventory, TenantFeature.MultipleWarehouses];
}
