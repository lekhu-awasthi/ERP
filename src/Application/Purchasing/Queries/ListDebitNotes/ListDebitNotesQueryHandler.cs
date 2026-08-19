using ErpApp.Application.Common.Pagination;
using ErpApp.Application.Common.Persistence;
using ErpApp.Domain.Purchasing;
using MediatR;

namespace ErpApp.Application.Purchasing.Queries.ListDebitNotes;

public sealed class ListDebitNotesQueryHandler(IAppDbContext db)
    : IRequestHandler<ListDebitNotesQuery, PagedResult<DebitNote>>
{
    public async Task<PagedResult<DebitNote>> Handle(ListDebitNotesQuery request, CancellationToken cancellationToken)
    {
        var query = db.DebitNotes.Where(x => x.OrganizationId == request.OrganizationId);

        if (request.Status is { } status)
        {
            query = query.Where(x => x.Status == status);
        }

        return await query.OrderByDescending(x => x.CreatedAt).ToPagedResultAsync(request.Page, request.PageSize, cancellationToken);
    }
}
