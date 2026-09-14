using ErpApp.Application.Common.Filtering;
using ErpApp.Application.Common.Locations;
using ErpApp.Application.Common.Pagination;
using ErpApp.Application.Common.Security;
using ErpApp.Domain.Sales;
using MediatR;

namespace ErpApp.Application.Sales.Queries.ListInvoices;

/// <param name="LocationId">Phase 32 -- filters the list to one billing location. Optional and
/// trailing: null means every location, which is what a tenant with one location always gets and what
/// every pre-phase-32 caller keeps getting. The grid's own LOCATION column needs no query change --
/// the list returns the <see cref="Invoice"/> itself, so <c>LocationId</c> is already on the wire, and
/// the client resolves the name from the location list its picker is populated from.</param>
/// <param name="Sort">Phase 40 -- the first consumer of the chrome's <c>Sort by</c> control, empty
/// since 34b built it. Null is <c>ListSort.Newest</c>, which is what every existing caller sends and
/// what the list has always done. The two members of <see cref="ListSort.DocumentOrderings"/> are the
/// two orderings <c>TenantIndexConvention</c> already indexes for a document, and that -- not a
/// preference -- is why there are two: see <see cref="ISortableQuery"/>.</param>
public sealed record ListInvoicesQuery(
    Guid OrganizationId,
    InvoiceStatus? Status,
    int Page = 1,
    int PageSize = PagingDefaults.DefaultPageSize,
    Guid? LocationId = null,
    string? Search = null,
    DateOnly? FromDate = null,
    DateOnly? ToDate = null,
    string? Sort = null)
    : IRequest<PagedResult<Invoice>>, IRequirePermission, IOrganizationScoped, ILocationFilteredQuery, ISearchableQuery, IDateRangeFilteredQuery, ISortableQuery
{
    public string PermissionKey => PermissionKeys.InvoiceView;
}
