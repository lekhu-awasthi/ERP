using ErpApp.Application.Common.Persistence;
using ErpApp.Domain.Tenancy;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.Tenancy.Queries.ListOrganizationMembers;

public sealed class ListOrganizationMembersQueryHandler(IAppDbContext db)
    : IRequestHandler<ListOrganizationMembersQuery, IReadOnlyList<OrganizationMemberDto>>
{
    public async Task<IReadOnlyList<OrganizationMemberDto>> Handle(
        ListOrganizationMembersQuery request, CancellationToken cancellationToken)
    {
        return await (
            from m in db.OrganizationMemberships
            join u in db.Users on m.UserId equals u.Id
            join r in db.Roles on m.RoleId equals r.Id
            where m.OrganizationId == request.OrganizationId && m.Status == MembershipStatus.Accepted
            orderby u.FullName
            select new OrganizationMemberDto(u.Id, u.FullName, u.Email, r.Name)
        ).ToListAsync(cancellationToken);
    }
}
