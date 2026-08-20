using ErpApp.Application.Common.Pagination;
using ErpApp.Application.Common.Security;
using MediatR;

namespace ErpApp.Application.Inventory.Queries.ListOpeningStockLines;

/// <summary>Backs the Opening Balances screen's Product tab, scoped to one Warehouse at a time
/// (this codebase's own first-class stock dimension -- no separate Location concept, see
/// docs/phase-17-status.md). Every TrackInventory product, with its opening line if one has been
/// set (0/0 otherwise).</summary>
public sealed record ListProductOpeningBalancesQuery(
    Guid OrganizationId,
    Guid WarehouseId,
    int Page = 1,
    int PageSize = PagingDefaults.DefaultPageSize)
    : IRequest<PagedResult<ProductOpeningBalanceDto>>, IRequirePermission, IOrganizationScoped
{
    public string PermissionKey => PermissionKeys.OpeningBalanceView;
}

public sealed record ProductOpeningBalanceDto(
    Guid ProductId, string ProductCode, string ProductName, string CategoryName, decimal Quantity, decimal Rate, decimal Amount);
