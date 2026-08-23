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
    int PageSize = PagingDefaults.DefaultPageSize)
    : IRequest<PagedResult<SalesOrder>>, IRequirePermission, IOrganizationScoped
{
    public string PermissionKey => PermissionKeys.SalesOrderView;
}
