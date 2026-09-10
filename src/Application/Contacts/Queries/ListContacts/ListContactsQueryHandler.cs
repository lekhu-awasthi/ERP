using ErpApp.Application.Common.Filtering;
using ErpApp.Application.Common.Pagination;
using ErpApp.Application.Common.Persistence;
using ErpApp.Domain.Contacts;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.Contacts.Queries.ListContacts;

public sealed class ListContactsQueryHandler(IAppDbContext db)
    : IRequestHandler<ListContactsQuery, PagedResult<Contact>>
{
    public async Task<PagedResult<Contact>> Handle(ListContactsQuery request, CancellationToken cancellationToken)
    {
        var query = db.Contacts.Where(x => x.OrganizationId == request.OrganizationId);

        if (request.Type is { } type)
        {
            query = query.Where(x => x.Type == type);
        }

        // Phase 34b (NFR-6.1) -- the list search. A separate composed `.Where()`, never folded
        // into one predicate with a null check: an expression tree does not short-circuit, so
        // `term == null || x.Code.Contains(term)` hands EF a null to translate on the unrestricted
        // branch, which is almost every caller (phase-33's gotcha).
        if (SearchTerm.Normalize(request.Search) is { } term)
        {
            query = query.Where(x => x.Name.Contains(term) || x.Code.Contains(term));
        }

        return await query.OrderBy(x => x.Name).ToPagedResultAsync(request.Page, request.PageSize, cancellationToken);
    }
}
