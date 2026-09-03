using ErpApp.Application.Common.Pagination;
using ErpApp.Application.Common.Security;
using ErpApp.Domain.Tenancy;
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
    : IRequest<PagedResult<ProductOpeningBalanceDto>>, IRequirePermission, IOrganizationScoped, IRequireFeature
{
    public string PermissionKey => PermissionKeys.OpeningBalanceView;

    // Phase 20f (FR-2.6): the Inventory context is only available to a tenant that opted
    // into Track Inventory. Catalog (Products/Categories/Units) is deliberately NOT gated --
    // live-confirmed that the reference product files those under Inventory in its nav but
    // every tenant needs them. See phase-20f-status.md.
    public IReadOnlyCollection<TenantFeature> RequiredFeatures => [TenantFeature.TrackInventory];
}

/// <summary>LineId (Phase 27a) is the OpeningStockLine's own Id, null until opening stock has been
/// set for this product in this warehouse. See AccountOpeningBalanceDto for why reporting tags need
/// it.</summary>
public sealed record ProductOpeningBalanceDto(
    Guid ProductId, string ProductCode, string ProductName, string CategoryName, decimal Quantity, decimal Rate,
    decimal Amount, Guid? LineId);
