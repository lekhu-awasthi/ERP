using ErpApp.Application.Common.Persistence;
using ErpApp.Application.Common.Security;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.Crm.Queries.ListDeals;

public sealed class ListDealsQueryHandler(IAppDbContext db, ICurrentUserService currentUser)
    : IRequestHandler<ListDealsQuery, DealListDto>
{
    public async Task<DealListDto> Handle(ListDealsQuery request, CancellationToken cancellationToken)
    {
        var userId = currentUser.UserId;

        var query = db.Deals.Where(x =>
            x.OrganizationId == request.OrganizationId
            // IsPrivate visibility -- hidden from everyone except the creator and any assignee,
            // extended from ListTasksQueryHandler's single-assignee check since Deal.Assignees is
            // genuinely plural (see ListDealsQuery's own doc comment).
            && (!x.IsPrivate || x.CreatedByUserId == userId || x.Assignees.Any(a => a.UserId == userId)));

        if (request.ContactId is { } contactId)
        {
            query = query.Where(x => x.ContactId == contactId);
        }

        if (request.Status is { } status)
        {
            query = query.Where(x => x.Status == status);
        }

        var totalCount = await query.CountAsync(cancellationToken);

        var rows = await query
            .Select(x => new
            {
                x.Id,
                x.Title,
                x.ContactId,
                x.LeadSourceId,
                x.Description,
                x.ExpectedRevenue,
                x.ExpectedClosingDate,
                x.StageId,
                x.Status,
                x.IsPrivate,
                x.ClosingDate,
                x.CreatedByUserId,
                x.CreatedAt,
                AssigneeUserIds = x.Assignees.Select(a => a.UserId).ToList(),
            })
            .OrderByDescending(x => x.CreatedAt)
            .Skip((request.Page - 1) * request.PageSize)
            .Take(request.PageSize)
            .ToListAsync(cancellationToken);

        var contactIds = rows.Select(x => x.ContactId).Distinct().ToList();
        var contactNames = await db.Contacts
            .Where(x => contactIds.Contains(x.Id))
            .Select(x => new { x.Id, x.Name })
            .ToDictionaryAsync(x => x.Id, x => x.Name, cancellationToken);

        var leadSourceIds = rows.Where(x => x.LeadSourceId is not null).Select(x => x.LeadSourceId!.Value).Distinct().ToList();
        var leadSourceNames = await db.LeadSources
            .Where(x => leadSourceIds.Contains(x.Id))
            .Select(x => new { x.Id, x.Name })
            .ToDictionaryAsync(x => x.Id, x => x.Name, cancellationToken);

        var stageIds = rows.Where(x => x.StageId is not null).Select(x => x.StageId!.Value).Distinct().ToList();
        var stages = await db.DealStages
            .Where(x => stageIds.Contains(x.Id))
            .Select(x => new { x.Id, x.Name, x.Color })
            .ToDictionaryAsync(x => x.Id, cancellationToken);

        var userIds = rows.Select(x => x.CreatedByUserId)
            .Concat(rows.SelectMany(x => x.AssigneeUserIds))
            .Distinct()
            .ToList();
        var userNames = await db.Users
            .Where(x => userIds.Contains(x.Id))
            .Select(x => new { x.Id, x.FullName })
            .ToDictionaryAsync(x => x.Id, x => x.FullName, cancellationToken);

        var dtoRows = rows.Select(x => new DealRowDto(
            x.Id,
            x.Title,
            x.ContactId,
            contactNames.GetValueOrDefault(x.ContactId, "—"),
            x.LeadSourceId,
            x.LeadSourceId is { } leadSourceId ? leadSourceNames.GetValueOrDefault(leadSourceId) : null,
            x.Description,
            x.ExpectedRevenue,
            x.ExpectedClosingDate,
            x.StageId,
            x.StageId is { } stageId ? stages.GetValueOrDefault(stageId)?.Name : null,
            x.StageId is { } stageId2 ? stages.GetValueOrDefault(stageId2)?.Color : null,
            x.Status,
            x.IsPrivate,
            x.ClosingDate,
            x.CreatedByUserId,
            userNames.GetValueOrDefault(x.CreatedByUserId, "—"),
            x.CreatedAt,
            x.AssigneeUserIds
                .Select(userId2 => new DealAssigneeDto(userId2, userNames.GetValueOrDefault(userId2, "—")))
                .ToList())
        ).ToList();

        return new DealListDto(dtoRows, request.Page, request.PageSize, totalCount);
    }
}
