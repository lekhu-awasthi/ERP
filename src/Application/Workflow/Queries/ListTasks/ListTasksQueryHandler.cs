using ErpApp.Application.Common.Filtering;
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
            // IsPrivate visibility -- a private task is hidden from everyone except its creator and
            // its assignee (see ListTasksQuery's own doc comment: this codebase's "no silently-inert
            // fields" precedent argues against a stored-but-unenforced IsPrivate flag).
            && (!x.IsPrivate || x.CreatedByUserId == userId || x.AssignedToUserId == userId));

        // Composed as separate Where clauses rather than folded into the predicate above: an
        // expression tree does not short-circuit, so `request.ParentId == null || x.ParentId ==
        // request.ParentId.Value` would hand EF a null to compare on the unscoped branch -- which is
        // every caller of the new screen (CLAUDE.md, phase-33).
        if (request.ParentType is { } parentType)
        {
            query = query.Where(x => x.ParentType == parentType);
        }

        if (request.ParentId is { } parentId)
        {
            query = query.Where(x => x.ParentId == parentId);
        }

        if (request.Status is { } status)
        {
            query = query.Where(x => x.Status == status);
        }

        // Inline Contains, never a shared matcher: a static call is untranslatable and so is the
        // StringComparison overload, and InMemory evaluates both in C# so every handler test would
        // pass while the endpoint 500s (phase-34b).
        if (SearchTerm.Normalize(request.Search) is { } term)
        {
            query = query.Where(x => x.Title.Contains(term));
        }

        var totalCount = await query.CountAsync(cancellationToken);

        var rows = await query
            .Select(x => new
            {
                x.Id, x.Title, x.Description, x.DueDate, x.CreatedAt, x.TaskTypeId, x.Priority, x.Status,
                x.IsPrivate, x.CreatedByUserId, x.AssignedToUserId,
            })
            .OrderBy(x => x.DueDate)
            .ThenByDescending(x => x.CreatedAt)
            .Skip((request.Page - 1) * request.PageSize)
            .Take(request.PageSize)
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

        return new TaskListDto(dtoRows, request.Page, request.PageSize, totalCount);
    }
}
