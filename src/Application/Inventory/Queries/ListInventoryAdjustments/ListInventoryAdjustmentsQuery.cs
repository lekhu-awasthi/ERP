using ErpApp.Application.Common.Filtering;
using ErpApp.Application.Common.Locations;
using ErpApp.Application.Common.Pagination;
using ErpApp.Application.Common.Security;
using ErpApp.Domain.Inventory;
using ErpApp.Domain.Tenancy;
using MediatR;

namespace ErpApp.Application.Inventory.Queries.ListInventoryAdjustments;

public sealed record ListInventoryAdjustmentsQuery(
    Guid OrganizationId,
    InventoryAdjustmentStatus? Status,
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
    Guid? LocationId = null,
    // Phase 47 -- the chrome's `Sort by`, swept from the one consumer phase 40 proved the seam
    // with. Optional and trailing, so null is this list's own default and every existing caller
    // keeps its behaviour. The two orderings are the two indexes `TenantIndexConvention` gives a
    // document -- see `ISortableQuery` for why that, and not taste, is what decides the menu.
    string? Sort = null)
    : IRequest<PagedResult<InventoryAdjustment>>, IRequirePermission, IOrganizationScoped, IRequireFeature, ILocationFilteredQuery, ISearchableQuery, IDateRangeFilteredQuery, ISortableQuery
{
    public string PermissionKey => PermissionKeys.InventoryAdjustmentView;

    // Phase 20f (FR-2.6): the Inventory context is only available to a tenant that opted
    // into Track Inventory. Catalog (Products/Categories/Units) is deliberately NOT gated --
    // live-confirmed that the reference product files those under Inventory in its nav but
    // every tenant needs them. See phase-20f-status.md.
    public IReadOnlyCollection<TenantFeature> RequiredFeatures => [TenantFeature.TrackInventory];
}
