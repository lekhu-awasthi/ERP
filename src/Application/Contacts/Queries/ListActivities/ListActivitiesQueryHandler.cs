using ErpApp.Application.Common.Persistence;
using ErpApp.Domain.Common;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.Contacts.Queries.ListActivities;

public sealed class ListActivitiesQueryHandler(IAppDbContext db) : IRequestHandler<ListActivitiesQuery, ActivityListDto>
{
    public async Task<ActivityListDto> Handle(ListActivitiesQuery request, CancellationToken cancellationToken)
    {
        var query = db.Audits.Where(x =>
            x.OrganizationId == request.OrganizationId
            && x.DocumentType == DocumentType.Contact
            && x.DocumentId == request.ContactId);

        var totalCount = await query.CountAsync(cancellationToken);

        var rows = await query
            .OrderByDescending(x => x.CreatedAt)
            .Skip((request.Page - 1) * request.PageSize)
            .Take(request.PageSize)
            .Select(x => new { x.Id, x.Action, x.UserId, x.CreatedAt })
            .ToListAsync(cancellationToken);

        var userIds = rows.Select(x => x.UserId).Distinct().ToList();
        var userNames = await db.Users
            .Where(x => userIds.Contains(x.Id))
            .Select(x => new { x.Id, x.FullName })
            .ToDictionaryAsync(x => x.Id, x => x.FullName, cancellationToken);

        var dtoRows = rows
            .Select(x => new ActivityRowDto(x.Id, x.Action, x.UserId, userNames.GetValueOrDefault(x.UserId, "—"), x.CreatedAt))
            .ToList();

        return new ActivityListDto(dtoRows, request.Page, request.PageSize, totalCount);
    }
}
