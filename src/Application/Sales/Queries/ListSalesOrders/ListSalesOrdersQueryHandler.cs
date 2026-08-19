using ErpApp.Application.Common.Pagination;
using ErpApp.Application.Common.Persistence;
using ErpApp.Domain.Sales;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.Sales.Queries.ListSalesOrders;

public sealed class ListSalesOrdersQueryHandler(IAppDbContext db)
    : IRequestHandler<ListSalesOrdersQuery, PagedResult<SalesOrder>>
{
    public async Task<PagedResult<SalesOrder>> Handle(ListSalesOrdersQuery request, CancellationToken cancellationToken)
    {
        var query = db.SalesOrders.Where(x => x.OrganizationId == request.OrganizationId);

        if (request.Status is { } status)
        {
            query = query.Where(x => x.Status == status);
        }

        return await query.OrderByDescending(x => x.CreatedAt)
            .ToPagedResultAsync(request.Page, request.PageSize, cancellationToken);
    }
}
