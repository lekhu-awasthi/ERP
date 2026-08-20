using ErpApp.Application.Common.Pagination;
using ErpApp.Domain.Sales;
using MediatR;

namespace ErpApp.Application.Sales.Queries.ListSalesOrders;

public sealed record ListSalesOrdersQuery(
    Guid OrganizationId,
    SalesOrderStatus? Status,
    int Page = 1,
    int PageSize = PagingDefaults.DefaultPageSize)
    : IRequest<PagedResult<SalesOrder>>;
