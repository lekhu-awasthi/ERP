using ErpApp.Application.Common.Pagination;
using ErpApp.Application.Common.Persistence;
using ErpApp.Domain.Sales;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.Sales.Queries.ListCreditNotes;

public sealed class ListCreditNotesQueryHandler(IAppDbContext db)
    : IRequestHandler<ListCreditNotesQuery, PagedResult<CreditNote>>
{
    public async Task<PagedResult<CreditNote>> Handle(ListCreditNotesQuery request, CancellationToken cancellationToken)
    {
        var query = db.CreditNotes.Where(x => x.OrganizationId == request.OrganizationId);

        if (request.Status is { } status)
        {
            query = query.Where(x => x.Status == status);
        }

        return await query.OrderByDescending(x => x.CreatedAt)
            .ToPagedResultAsync(request.Page, request.PageSize, cancellationToken);
    }
}
