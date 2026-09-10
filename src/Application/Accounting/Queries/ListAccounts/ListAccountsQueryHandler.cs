using ErpApp.Application.Common.Filtering;
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

        // Phase 34b (NFR-6.1) -- the list search. A separate composed `.Where()`, never folded
        // into one predicate with a null check: an expression tree does not short-circuit, so
        // `term == null || x.Code.Contains(term)` hands EF a null to translate on the unrestricted
        // branch, which is almost every caller (phase-33's gotcha).
        if (SearchTerm.Normalize(request.Search) is { } term)
        {
            query = query.Where(x => x.Name.Contains(term) || x.Code.Contains(term));
        }

        return await query.OrderBy(x => x.Code).ToPagedResultAsync(request.Page, request.PageSize, cancellationToken);
    }
}
