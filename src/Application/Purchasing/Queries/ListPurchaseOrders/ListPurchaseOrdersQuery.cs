using ErpApp.Application.Common.Pagination;
using ErpApp.Domain.Purchasing;
using MediatR;

namespace ErpApp.Application.Purchasing.Queries.ListPurchaseOrders;

public sealed record ListPurchaseOrdersQuery(
    Guid OrganizationId,
    PurchaseOrderStatus? Status,
    int Page = 1,
    int PageSize = PagingDefaults.DefaultPageSize)
    : IRequest<PagedResult<PurchaseOrder>>;
