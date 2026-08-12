using ErpApp.Application.Common.Persistence;
using ErpApp.Domain.Purchasing;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.Purchasing.Queries.ListDebitNotes;

public sealed class ListDebitNotesQueryHandler(IAppDbContext db)
    : IRequestHandler<ListDebitNotesQuery, IReadOnlyList<DebitNote>>
{
    public async Task<IReadOnlyList<DebitNote>> Handle(ListDebitNotesQuery request, CancellationToken cancellationToken)
    {
        var query = db.DebitNotes.Where(x => x.OrganizationId == request.OrganizationId);

        if (request.Status is { } status)
        {
            query = query.Where(x => x.Status == status);
        }

        return await query.OrderByDescending(x => x.CreatedAt).ToListAsync(cancellationToken);
    }
}
