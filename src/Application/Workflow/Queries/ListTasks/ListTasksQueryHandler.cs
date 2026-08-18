using ErpApp.Application.Common.Persistence;
using ErpApp.Application.Common.Security;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.Workflow.Queries.ListTasks;

public sealed class ListTasksQueryHandler(IAppDbContext db, ICurrentUserService currentUser)
    : IRequestHandler<ListTasksQuery, TaskListDto>
{
    public async Task<TaskListDto> Handle(ListTasksQuery request, CancellationToken cancellationToken)
    {
        var userId = currentUser.UserId;

        var query = db.Tasks.Where(x =>
            x.OrganizationId == request.OrganizationId
            && x.ParentType == request.ParentType
            && x.ParentId == request.ParentId
            // IsPrivate visibility -- a private task is hidden from everyone except its creator and
            // its assignee (see ListTasksQuery's own doc comment: this codebase's "no silently-inert
            // fields" precedent argues against a stored-but-unenforced IsPrivate flag).
            && (!x.IsPrivate || x.CreatedByUserId == userId || x.AssignedToUserId == userId));

        if (request.Status is { } status)
        {
            query = query.Where(x => x.Status == status);
        }

        var rows = await query
            .Select(x => new
            {
                x.Id, x.Title, x.Description, x.DueDate, x.CreatedAt, x.TaskTypeId, x.Priority, x.Status,
                x.IsPrivate, x.CreatedByUserId, x.AssignedToUserId,
            })
            .OrderBy(x => x.DueDate)
            .ThenByDescending(x => x.CreatedAt)
            .ToListAsync(cancellationToken);

        var taskTypeIds = rows.Select(x => x.TaskTypeId).Distinct().ToList();
        var taskTypes = await db.TaskTypes
            .Where(x => x.OrganizationId == request.OrganizationId && taskTypeIds.Contains(x.Id))
            .Select(x => new { x.Id, x.Name, x.Color })
            .ToDictionaryAsync(x => x.Id, cancellationToken);

        var userIds = rows.Select(x => x.CreatedByUserId)
            .Concat(rows.Where(x => x.AssignedToUserId is not null).Select(x => x.AssignedToUserId!.Value))
            .Distinct()
            .ToList();
        var userNames = await db.Users
            .Where(x => userIds.Contains(x.Id))
            .Select(x => new { x.Id, x.FullName })
            .ToDictionaryAsync(x => x.Id, x => x.FullName, cancellationToken);

        var dtoRows = rows.Select(x => new TaskRowDto(
            x.Id,
            x.Title,
            x.Description,
            x.DueDate,
            x.CreatedAt,
            x.TaskTypeId,
            taskTypes.GetValueOrDefault(x.TaskTypeId)?.Name ?? "—",
            taskTypes.GetValueOrDefault(x.TaskTypeId)?.Color ?? "#6c757d",
            x.Priority,
            x.Status,
            x.IsPrivate,
            x.CreatedByUserId,
            userNames.GetValueOrDefault(x.CreatedByUserId, "—"),
            x.AssignedToUserId,
            x.AssignedToUserId is { } assigneeId ? userNames.GetValueOrDefault(assigneeId, "—") : null))
            .ToList();

        return new TaskListDto(dtoRows);
    }
}
