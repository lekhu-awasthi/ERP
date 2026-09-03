using ErpApp.Application.Accounting.Reports;
using ErpApp.Application.Common.Pagination;
using ErpApp.Application.Common.Persistence;
using ErpApp.Domain.Identity;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.Identity.Queries.UserLog;

public sealed class UserLogQueryHandler(IAppDbContext db) : IRequestHandler<UserLogQuery, UserLogDto>
{
    public async Task<UserLogDto> Handle(UserLogQuery request, CancellationToken cancellationToken)
    {
        // The organization's members, which is what turns a tenant-less event into a tenant-scoped
        // report -- see UserLogQuery's remarks. Every membership row that names a user counts,
        // whatever its status: someone whose membership was later revoked still signed in while it
        // was live, and dropping those rows would quietly hide the sessions an Admin most wants.
        var memberUserIds = await db.OrganizationMemberships
            .Where(m => m.OrganizationId == request.OrganizationId && m.UserId != null)
            .Select(m => m.UserId!.Value)
            .Distinct()
            .ToListAsync(cancellationToken);

        if (request.UserId is { } filteredUserId)
        {
            // Asking for a non-member returns nothing rather than everything.
            memberUserIds = memberUserIds.Where(id => id == filteredUserId).ToList();
        }

        var members = await db.Users
            .Where(u => memberUserIds.Contains(u.Id))
            .Select(u => new { u.Id, u.FullName, u.Email })
            .ToListAsync(cancellationToken);
        var membersById = members.ToDictionary(x => x.Id);
        var memberEmails = members.Select(x => x.Email).ToList();

        // PostedAt-style boundaries: OccurredAt is a UTC DateTimeOffset, the report filters on a
        // DateOnly range, and GlDateBoundary is the one place that conversion is defined.
        var from = GlDateBoundary.StartOfDayUtc(request.FromDate);
        var to = GlDateBoundary.EndOfDayUtc(request.ToDate);

        var events = await db.UserLoginEvents
            .Where(e => e.OccurredAt >= from && e.OccurredAt <= to
                && ((e.UserId != null && memberUserIds.Contains(e.UserId.Value))
                    || (e.UserId == null && memberEmails.Contains(e.Email))))
            .ToListAsync(cancellationToken);

        var rows = events
            .OrderByDescending(e => e.OccurredAt)
            .ThenBy(e => e.Id)
            .Select(e =>
            {
                var member = e.UserId is { } id ? membersById.GetValueOrDefault(id) : null;
                return new UserLogRowDto(
                    e.Id,
                    e.UserId,
                    // The live report prints the email in the Full Name column for a user with no
                    // name set; a failed attempt has no user at all, and the same fallback is the
                    // only honest answer there too.
                    string.IsNullOrWhiteSpace(member?.FullName) ? e.Email : member!.FullName,
                    member?.Email ?? e.Email,
                    e.OccurredAt,
                    e.DeviceOs,
                    e.IpAddress,
                    e.Outcome,
                    DescriptionFor(e.Outcome),
                    e.Browser);
            })
            .ToList();

        var paged = request.ExportAll ? rows.ToUnpagedResult() : rows.ToPagedResult(request.Page, request.PageSize);

        // No footer total: there is nothing to add up. Same call phase-26a made on the Transaction
        // list, and for the same reason.
        return new UserLogDto(
            request.FromDate, request.ToDate, paged.Items, paged.Page, paged.PageSize, paged.TotalCount);
    }

    /// <summary>The reference product's Description column, verbatim.</summary>
    private static string DescriptionFor(UserLoginOutcome outcome) => outcome switch
    {
        UserLoginOutcome.LoginSucceeded => "Login Success",
        UserLoginOutcome.LoginFailed => "Login Fail",
        UserLoginOutcome.LogoutSucceeded => "Logout Success",
        _ => outcome.ToString(),
    };
}
