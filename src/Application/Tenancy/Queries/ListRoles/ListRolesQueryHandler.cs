using ErpApp.Application.Common.Filtering;
using ErpApp.Application.Common.Pagination;
using ErpApp.Application.Common.Persistence;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.Tenancy.Queries.ListRoles;

public sealed class ListRolesQueryHandler(IAppDbContext db) : IRequestHandler<ListRolesQuery, PagedResult<RoleDto>>
{
    public async Task<PagedResult<RoleDto>> Handle(ListRolesQuery request, CancellationToken cancellationToken)
    {
        var query = db.Roles
            .Where(r => r.OrganizationId == null || r.OrganizationId == request.OrganizationId);

        // Phase 34b (NFR-6.1) -- the list search.
        if (SearchTerm.Normalize(request.Search) is { } term)
        {
            query = query.Where(r => r.Name.Contains(term) || (r.Description != null && r.Description.Contains(term)));
        }

        return await query
            .OrderBy(r => r.OrganizationId == null ? 0 : 1)
            .ThenBy(r => r.Name)
            .Select(r => new RoleDto(r.Id, r.Name, r.Description, r.OrganizationId == null))
            .ToPagedResultAsync(request.Page, request.PageSize, cancellationToken);
    }
}
