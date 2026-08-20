using ErpApp.Application.Common.Pagination;
using ErpApp.Application.Common.Persistence;
using ErpApp.Domain.Sales;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.Sales.Queries.ListInvoices;

public sealed class ListInvoicesQueryHandler(IAppDbContext db)
    : IRequestHandler<ListInvoicesQuery, PagedResult<Invoice>>
{
    public async Task<PagedResult<Invoice>> Handle(ListInvoicesQuery request, CancellationToken cancellationToken)
    {
        var query = db.Invoices.Where(x => x.OrganizationId == request.OrganizationId);

        if (request.Status is { } status)
        {
            query = query.Where(x => x.Status == status);
        }

        return await query.OrderByDescending(x => x.CreatedAt)
            .ToPagedResultAsync(request.Page, request.PageSize, cancellationToken);
    }
}
