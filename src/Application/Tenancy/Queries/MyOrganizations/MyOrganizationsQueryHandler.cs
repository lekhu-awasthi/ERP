using ErpApp.Application.Common.Persistence;
using ErpApp.Application.Common.Security;
using ErpApp.Domain.Tenancy;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.Tenancy.Queries.MyOrganizations;

public sealed class MyOrganizationsQueryHandler(IAppDbContext db, ICurrentUserService currentUser, TimeProvider timeProvider)
    : IRequestHandler<MyOrganizationsQuery, MyOrganizationsResult>
{
    public async Task<MyOrganizationsResult> Handle(MyOrganizationsQuery request, CancellationToken cancellationToken)
    {
        var user = await db.Users.SingleAsync(u => u.Id == currentUser.UserId, cancellationToken);

        // The term end comes back as a nullable projection and "has it ended" is decided in memory
        // afterwards, rather than comparing against a captured `now` inside the query: the comparison
        // is the same sentence SubscriptionExpiryBehavior enforces, and it is worth being able to read
        // it beside that one rather than through a translated expression.
        var rows = await (
            from m in db.OrganizationMemberships
            join o in db.Organizations on m.OrganizationId equals o.Id
            join r in db.Roles on m.RoleId equals r.Id
            where m.UserId == currentUser.UserId && m.Status == MembershipStatus.Accepted
            select new
            {
                o.Id,
                o.Name,
                o.WorkspaceName,
                o.Industry,
                Role = r.Name,
                TermEndsAt = db.TenantSubscriptions
                    .Where(s => s.OrganizationId == o.Id)
                    .Select(s => (DateTimeOffset?)s.TermEndsAt)
                    .FirstOrDefault(),
            }
        ).ToListAsync(cancellationToken);

        var now = timeProvider.GetUtcNow();

        var organizations = rows
            .Select(x => new OrganizationSummaryDto(
                x.Id,
                x.Name,
                x.WorkspaceName,
                x.Industry,
                x.Role,
                x.TermEndsAt,
                // A tenant with no subscription row is not expired -- the same reading
                // SubscriptionExpiryBehavior takes, for the same reason: a missing row means a
                // fixture or a partially migrated tenant, and failing those closed would break far
                // more than it protects. Kept identical on purpose; a picker that marked a tenant
                // expired while every write still succeeded would be worse than saying nothing.
                x.TermEndsAt is { } endsAt && endsAt <= now))
            .ToList();

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
