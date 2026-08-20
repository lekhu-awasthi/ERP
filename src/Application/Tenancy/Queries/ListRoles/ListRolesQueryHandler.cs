using ErpApp.Application.Common.Pagination;
using ErpApp.Application.Common.Persistence;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.Tenancy.Queries.ListRoles;

public sealed class ListRolesQueryHandler(IAppDbContext db) : IRequestHandler<ListRolesQuery, PagedResult<RoleDto>>
{
    public async Task<PagedResult<RoleDto>> Handle(ListRolesQuery request, CancellationToken cancellationToken)
    {
        return await db.Roles
            .Where(r => r.OrganizationId == null || r.OrganizationId == request.OrganizationId)
            .OrderBy(r => r.OrganizationId == null ? 0 : 1)
            .ThenBy(r => r.Name)
            .Select(r => new RoleDto(r.Id, r.Name, r.Description, r.OrganizationId == null))
            .ToPagedResultAsync(request.Page, request.PageSize, cancellationToken);
    }
}
