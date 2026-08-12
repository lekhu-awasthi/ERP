using ErpApp.Application.Common.Persistence;
using ErpApp.Domain.Sales;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.Sales.Queries.ListInvoices;

public sealed class ListInvoicesQueryHandler(IAppDbContext db)
    : IRequestHandler<ListInvoicesQuery, IReadOnlyList<Invoice>>
{
    public async Task<IReadOnlyList<Invoice>> Handle(ListInvoicesQuery request, CancellationToken cancellationToken)
    {
        var query = db.Invoices.Where(x => x.OrganizationId == request.OrganizationId);

        if (request.Status is { } status)
        {
            query = query.Where(x => x.Status == status);
        }

        return await query.OrderByDescending(x => x.CreatedAt).ToListAsync(cancellationToken);
    }
}
