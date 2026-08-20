using ErpApp.Application.Common.Pagination;
using ErpApp.Application.Common.Persistence;
using ErpApp.Domain.Purchasing;
using MediatR;

namespace ErpApp.Application.Purchasing.Queries.ListPurchaseOrders;

public sealed class ListPurchaseOrdersQueryHandler(IAppDbContext db)
    : IRequestHandler<ListPurchaseOrdersQuery, PagedResult<PurchaseOrder>>
{
    public async Task<PagedResult<PurchaseOrder>> Handle(ListPurchaseOrdersQuery request, CancellationToken cancellationToken)
    {
        var query = db.PurchaseOrders.Where(x => x.OrganizationId == request.OrganizationId);

        if (request.Status is { } status)
        {
            query = query.Where(x => x.Status == status);
        }

        return await query.OrderByDescending(x => x.CreatedAt).ToPagedResultAsync(request.Page, request.PageSize, cancellationToken);
    }
}
