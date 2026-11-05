using ErpApp.Application.Common.Persistence;
using ErpApp.Domain.Tenancy;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.Tenancy.Queries.ListPendingInvitations;

public sealed class ListPendingInvitationsQueryHandler(IAppDbContext db)
    : IRequestHandler<ListPendingInvitationsQuery, IReadOnlyList<PendingSentInvitationDto>>
{
    public async Task<IReadOnlyList<PendingSentInvitationDto>> Handle(
        ListPendingInvitationsQuery request, CancellationToken cancellationToken)
    {
        // An invitation is addressed to an email; UserId is filled in only when the address already
        // had an account at sending time, or at acceptance. So "registered" is read from Users by
        // email, not from the row's UserId, which stays null for someone who signed up afterwards.
        var rows = await (
            from m in db.OrganizationMemberships
            join r in db.Roles on m.RoleId equals r.Id
            where m.OrganizationId == request.OrganizationId && m.Status == MembershipStatus.Invited
            orderby m.CreatedAt descending
            select new
            {
                m.Id,
                m.InvitedEmail,
                m.UserId,
                m.RoleId,
                RoleName = r.Name,
                m.CreatedAt,
                Registered = m.UserId != null || db.Users.Any(u => u.Email == m.InvitedEmail),
            }
        ).ToListAsync(cancellationToken);

        return rows
            .Select(x => new PendingSentInvitationDto(
                x.Id, x.InvitedEmail ?? string.Empty, x.RoleId, x.RoleName, x.CreatedAt, x.Registered))
            .ToList();
    }
}
