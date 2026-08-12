using ErpApp.Application.Common.Persistence;
using ErpApp.Domain.Sales;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.Sales.Queries.ListSalesOrders;

public sealed class ListSalesOrdersQueryHandler(IAppDbContext db)
    : IRequestHandler<ListSalesOrdersQuery, IReadOnlyList<SalesOrder>>
{
    public async Task<IReadOnlyList<SalesOrder>> Handle(ListSalesOrdersQuery request, CancellationToken cancellationToken)
    {
        var query = db.SalesOrders.Where(x => x.OrganizationId == request.OrganizationId);

        if (request.Status is { } status)
        {
            query = query.Where(x => x.Status == status);
        }

        return await query.OrderByDescending(x => x.CreatedAt).ToListAsync(cancellationToken);
    }
}
