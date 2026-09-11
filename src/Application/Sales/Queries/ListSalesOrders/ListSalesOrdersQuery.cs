using ErpApp.Application.Common.Filtering;
using ErpApp.Application.Common.Locations;
using ErpApp.Application.Common.Pagination;
using ErpApp.Application.Common.Security;
using ErpApp.Domain.Sales;
using MediatR;

namespace ErpApp.Application.Sales.Queries.ListSalesOrders;

/// <summary>
/// Fixed as part of Phase 18: this query was missing IOrganizationScoped/IRequirePermission --
/// the sibling ListQuotations/ListInvoices/ListCreditNotes/ListPurchaseOrders/ListPurchaseBills/
/// ListDebitNotes/ListExpenses/ListPayments queries have the exact same gap (confirmed by grep),
/// a pre-existing, codebase-wide tenant-isolation issue that predates this phase and spans modules
/// outside its scope -- fixed here only because Phase 18 is the first phase to give
/// ListSalesOrdersQuery a real caller (the new Sales Order Angular page). The other 8 queries are
/// flagged as a separate, urgent follow-up (see docs/phase-18-status.md) rather than fixed
/// silently alongside this one.
/// </summary>
public sealed record ListSalesOrdersQuery(
    Guid OrganizationId,
    SalesOrderStatus? Status,
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
    : IRequest<PagedResult<SalesOrder>>, IRequirePermission, IOrganizationScoped, ILocationFilteredQuery, ISearchableQuery, IDateRangeFilteredQuery
{
    public string PermissionKey => PermissionKeys.SalesOrderView;
}
