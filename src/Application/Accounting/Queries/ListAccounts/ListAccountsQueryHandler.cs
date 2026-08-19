using ErpApp.Application.Common.Pagination;
using ErpApp.Application.Common.Persistence;
using ErpApp.Domain.Accounting;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.Accounting.Queries.ListAccounts;

public sealed class ListAccountsQueryHandler(IAppDbContext db) : IRequestHandler<ListAccountsQuery, PagedResult<Account>>
{
    public async Task<PagedResult<Account>> Handle(ListAccountsQuery request, CancellationToken cancellationToken)
    {
        var query = db.Accounts.Where(x => x.OrganizationId == request.OrganizationId);

        if (request.RootType is { } rootType)
        {
            query = query.Where(x => x.RootType == rootType);
        }

        return await query.OrderBy(x => x.Code).ToPagedResultAsync(request.Page, request.PageSize, cancellationToken);
    }
}
