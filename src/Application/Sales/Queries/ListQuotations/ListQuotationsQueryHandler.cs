using ErpApp.Application.Common.Persistence;
using ErpApp.Domain.Sales;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.Sales.Queries.ListQuotations;

public sealed class ListQuotationsQueryHandler(IAppDbContext db)
    : IRequestHandler<ListQuotationsQuery, IReadOnlyList<Quotation>>
{
    public async Task<IReadOnlyList<Quotation>> Handle(ListQuotationsQuery request, CancellationToken cancellationToken)
    {
        var query = db.Quotations.Where(x => x.OrganizationId == request.OrganizationId);

        if (request.Status is { } status)
        {
            query = query.Where(x => x.Status == status);
        }

        return await query.OrderByDescending(x => x.CreatedAt).ToListAsync(cancellationToken);
    }
}
