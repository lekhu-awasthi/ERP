using ErpApp.Application.Common.Filtering;
using ErpApp.Application.Common.Locations;
using ErpApp.Application.Common.Pagination;
using ErpApp.Application.Common.Security;
using ErpApp.Domain.Sales;
using MediatR;

namespace ErpApp.Application.Sales.Queries.ListDeliveryNotes;

/// <summary>Phase 58 -- the document-list shape every sales list has: status tab, search, the shell's
/// date range, the Billing Location filter and the two indexed orderings.</summary>
public sealed record ListDeliveryNotesQuery(
    Guid OrganizationId,
    DeliveryNoteStatus? Status,
    int Page = 1,
    int PageSize = PagingDefaults.DefaultPageSize,
    string? Search = null,
    DateOnly? FromDate = null,
    DateOnly? ToDate = null,
    Guid? LocationId = null,
    string? Sort = null)
    : IRequest<PagedResult<DeliveryNote>>, IRequirePermission, IOrganizationScoped, ILocationFilteredQuery, ISearchableQuery, IDateRangeFilteredQuery, ISortableQuery
{
    public string PermissionKey => PermissionKeys.DeliveryNoteView;
}
