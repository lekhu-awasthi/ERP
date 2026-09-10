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
public sealed record ListInvoicesQuery(
    Guid OrganizationId,
    InvoiceStatus? Status,
    int Page = 1,
    int PageSize = PagingDefaults.DefaultPageSize,
    Guid? LocationId = null,
    string? Search = null,
    DateOnly? FromDate = null,
    DateOnly? ToDate = null)
    : IRequest<PagedResult<Invoice>>, IRequirePermission, IOrganizationScoped, ILocationFilteredQuery, ISearchableQuery, IDateRangeFilteredQuery
{
    public string PermissionKey => PermissionKeys.InvoiceView;
}
