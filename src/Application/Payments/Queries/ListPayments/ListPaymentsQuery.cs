using ErpApp.Application.Common.Pagination;
using ErpApp.Domain.Payments;
using MediatR;

namespace ErpApp.Application.Payments.Queries.ListPayments;

/// <summary>
/// Direction (Phase 16c) -- previously unfiltered server-side, with both list pages (Sales'
/// payment-list-page for Received, Purchasing's supplier-payment-list-page for Paid) filtering the
/// full unpaginated result client-side after the fact. That breaks the moment this query is
/// actually paginated: a page could legitimately come back with zero rows matching the caller's
/// direction while more exist on a later page, and the reported TotalCount would count the wrong
/// direction's rows too. Filtering server-side (like every other list query's Status filter) fixes
/// both.
/// </summary>
public sealed record ListPaymentsQuery(
    Guid OrganizationId,
    PaymentStatus? Status,
    PaymentDirection? Direction = null,
    int Page = 1,
    int PageSize = PagingDefaults.DefaultPageSize)
    : IRequest<PagedResult<Payment>>;
