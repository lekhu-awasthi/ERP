using ErpApp.Application.Common.Pagination;
using ErpApp.Application.Common.Persistence;
using ErpApp.Domain.Tenancy;
using MediatR;

namespace ErpApp.Application.Tenancy.Queries.ListOrganizationMembers;

public sealed class ListOrganizationMembersQueryHandler(IAppDbContext db)
    : IRequestHandler<ListOrganizationMembersQuery, PagedResult<OrganizationMemberDto>>
{
    public async Task<PagedResult<OrganizationMemberDto>> Handle(
        ListOrganizationMembersQuery request, CancellationToken cancellationToken)
    {
        var query =
            from m in db.OrganizationMemberships
            join u in db.Users on m.UserId equals u.Id
            join r in db.Roles on m.RoleId equals r.Id
            where m.OrganizationId == request.OrganizationId && m.Status == MembershipStatus.Accepted
            orderby u.FullName
            select new OrganizationMemberDto(m.Id, u.Id, u.FullName, u.Email, r.Id, r.Name);

        return await query.ToPagedResultAsync(request.Page, request.PageSize, cancellationToken);
    }
}
