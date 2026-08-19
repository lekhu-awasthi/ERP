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

        return await query.OrderBy(x => x.Name).ToPagedResultAsync(request.Page, request.PageSize, cancellationToken);
    }
}
