using ErpApp.Application.Common.Persistence;
using ErpApp.Domain.Purchasing;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.Purchasing.Queries.ListPurchaseOrders;

public sealed class ListPurchaseOrdersQueryHandler(IAppDbContext db)
    : IRequestHandler<ListPurchaseOrdersQuery, IReadOnlyList<PurchaseOrder>>
{
    public async Task<IReadOnlyList<PurchaseOrder>> Handle(ListPurchaseOrdersQuery request, CancellationToken cancellationToken)
    {
        var query = db.PurchaseOrders.Where(x => x.OrganizationId == request.OrganizationId);

        if (request.Status is { } status)
        {
            query = query.Where(x => x.Status == status);
        }

        return await query.OrderByDescending(x => x.CreatedAt).ToListAsync(cancellationToken);
    }
}
