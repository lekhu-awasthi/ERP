using ErpApp.Application.Common.Persistence;
using ErpApp.Application.Common.Security;
using ErpApp.Domain.Tenancy;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.Tenancy.Queries.MyOrganizations;

public sealed class MyOrganizationsQueryHandler(IAppDbContext db, ICurrentUserService currentUser)
    : IRequestHandler<MyOrganizationsQuery, MyOrganizationsResult>
{
    public async Task<MyOrganizationsResult> Handle(MyOrganizationsQuery request, CancellationToken cancellationToken)
    {
        var user = await db.Users.SingleAsync(u => u.Id == currentUser.UserId, cancellationToken);

        var organizations = await (
            from m in db.OrganizationMemberships
            join o in db.Organizations on m.OrganizationId equals o.Id
            join r in db.Roles on m.RoleId equals r.Id
            where m.UserId == currentUser.UserId && m.Status == MembershipStatus.Accepted
            select new OrganizationSummaryDto(o.Id, o.Name, o.WorkspaceName, o.Industry, r.Name)
        ).ToListAsync(cancellationToken);

        var requests = await (
            from m in db.OrganizationMemberships
            join o in db.Organizations on m.OrganizationId equals o.Id
            where m.UserId == currentUser.UserId && m.Status == MembershipStatus.Requested
            select new PendingRequestDto(m.Id, o.Id, o.Name, m.CreatedAt)
        ).ToListAsync(cancellationToken);

        var invitations = await (
            from m in db.OrganizationMemberships
            join o in db.Organizations on m.OrganizationId equals o.Id
            join r in db.Roles on m.RoleId equals r.Id
            where m.Status == MembershipStatus.Invited
                  && (m.UserId == currentUser.UserId || (m.UserId == null && m.InvitedEmail == user.Email))
            select new PendingInvitationDto(m.Id, o.Id, o.Name, r.Name, m.CreatedAt)
        ).ToListAsync(cancellationToken);

        return new MyOrganizationsResult(organizations, requests, invitations);
    }
}
