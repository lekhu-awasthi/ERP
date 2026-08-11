using ErpApp.Application.Common.Persistence;
using ErpApp.Domain.Accounting;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.Accounting.Queries.ListAccounts;

public sealed class ListAccountsQueryHandler(IAppDbContext db) : IRequestHandler<ListAccountsQuery, IReadOnlyList<Account>>
{
    public async Task<IReadOnlyList<Account>> Handle(ListAccountsQuery request, CancellationToken cancellationToken)
    {
        var query = db.Accounts.Where(x => x.OrganizationId == request.OrganizationId);

        if (request.RootType is { } rootType)
        {
            query = query.Where(x => x.RootType == rootType);
        }

        return await query.OrderBy(x => x.Code).ToListAsync(cancellationToken);
    }
}
