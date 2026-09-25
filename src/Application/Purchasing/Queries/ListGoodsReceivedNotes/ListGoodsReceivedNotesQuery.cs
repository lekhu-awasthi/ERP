using ErpApp.Application.Common.Filtering;
using ErpApp.Application.Common.Locations;
using ErpApp.Application.Common.Pagination;
using ErpApp.Application.Common.Security;
using ErpApp.Domain.Purchasing;
using MediatR;

namespace ErpApp.Application.Purchasing.Queries.ListGoodsReceivedNotes;

/// <summary>Phase 58 -- the <c>ListPurchaseOrdersQuery</c> shape exactly: status tab, search over
/// code/reference/tracking number, the shell's date range, the Billing Location filter and the two
/// indexed orderings.</summary>
public sealed record ListGoodsReceivedNotesQuery(
    Guid OrganizationId,
    GoodsReceivedNoteStatus? Status,
    int Page = 1,
    int PageSize = PagingDefaults.DefaultPageSize,
    string? Search = null,
    DateOnly? FromDate = null,
    DateOnly? ToDate = null,
    Guid? LocationId = null,
    string? Sort = null)
    : IRequest<PagedResult<GoodsReceivedNote>>, IRequirePermission, IOrganizationScoped, ILocationFilteredQuery, ISearchableQuery, IDateRangeFilteredQuery, ISortableQuery
{
    public string PermissionKey => PermissionKeys.GoodsReceivedNoteView;
}
