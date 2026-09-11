using ErpApp.Application.Common.Filtering;
using ErpApp.Application.Common.Locations;
using ErpApp.Application.Common.Pagination;
using ErpApp.Application.Common.Security;
using ErpApp.Domain.Manufacturing;
using ErpApp.Domain.Tenancy;
using MediatR;

namespace ErpApp.Application.Manufacturing.Queries.ListProductionOrders;

public sealed record ListProductionOrdersQuery(
    Guid OrganizationId,
    ProductionOrderStatus? Status,
    int Page = 1,
    int PageSize = PagingDefaults.DefaultPageSize,
    string? Search = null,
    DateOnly? FromDate = null,
    DateOnly? ToDate = null,
    // Phase 35a -- the list's own Billing Location filter, the control the reference product
    // renders as a funnel on the LOCATION column every document grid carries (confirm-live
    // 2026-09-10). Independent of phase 32b's permission scope above it: a caller may hold every
    // location and still want one branch's rows. Optional and trailing, so null is "All" and
    // every pre-phase-35 caller keeps its behaviour.
    Guid? LocationId = null)
    : IRequest<PagedResult<ProductionOrderListItemDto>>, IRequirePermission, IOrganizationScoped, IRequireFeature, ILocationFilteredQuery, ISearchableQuery, IDateRangeFilteredQuery
{
    public string PermissionKey => PermissionKeys.ProductionOrderView;

    public IReadOnlyCollection<TenantFeature> RequiredFeatures =>
        [TenantFeature.Manufacturing, TenantFeature.TrackInventory];
}

/// <summary>CustomStatusId was added in Phase 27a: the Production Order list grid carries a
/// per-row custom-status picker in the reference product (labelled STATUS where Sales Order says
/// STAGE -- the same control over the same lookup), and this projection is what feeds it.</summary>
public sealed record ProductionOrderListItemDto(
    Guid Id, string Code, DateOnly Date, string? Reference, Guid ProductId, string ProductName,
    decimal OutputQuantity, ProductionOrderStatus Status, Guid? CustomStatusId,
    // Phase 35a -- the LOCATION column every live document grid carries. The other thirteen lists
    // return the aggregate, so the field came free; these two project a DTO and had to be told.
    Guid? LocationId);
