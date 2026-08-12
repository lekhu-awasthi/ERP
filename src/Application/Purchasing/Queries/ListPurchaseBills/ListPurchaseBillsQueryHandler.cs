using ErpApp.Application.Common.Persistence;
using ErpApp.Domain.Purchasing;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.Purchasing.Queries.ListPurchaseBills;

public sealed class ListPurchaseBillsQueryHandler(IAppDbContext db)
    : IRequestHandler<ListPurchaseBillsQuery, IReadOnlyList<PurchaseBill>>
{
    public async Task<IReadOnlyList<PurchaseBill>> Handle(ListPurchaseBillsQuery request, CancellationToken cancellationToken)
    {
        var query = db.PurchaseBills.Where(x => x.OrganizationId == request.OrganizationId);

        if (request.Status is { } status)
        {
            query = query.Where(x => x.Status == status);
        }

        return await query.OrderByDescending(x => x.CreatedAt).ToListAsync(cancellationToken);
    }
}
