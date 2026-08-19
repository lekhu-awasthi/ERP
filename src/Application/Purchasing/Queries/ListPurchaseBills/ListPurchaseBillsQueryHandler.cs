using ErpApp.Application.Common.Pagination;
using ErpApp.Application.Common.Persistence;
using ErpApp.Domain.Purchasing;
using MediatR;

namespace ErpApp.Application.Purchasing.Queries.ListPurchaseBills;

public sealed class ListPurchaseBillsQueryHandler(IAppDbContext db)
    : IRequestHandler<ListPurchaseBillsQuery, PagedResult<PurchaseBill>>
{
    public async Task<PagedResult<PurchaseBill>> Handle(ListPurchaseBillsQuery request, CancellationToken cancellationToken)
    {
        var query = db.PurchaseBills.Where(x => x.OrganizationId == request.OrganizationId);

        if (request.Status is { } status)
        {
            query = query.Where(x => x.Status == status);
        }

        return await query.OrderByDescending(x => x.CreatedAt).ToPagedResultAsync(request.Page, request.PageSize, cancellationToken);
    }
}
