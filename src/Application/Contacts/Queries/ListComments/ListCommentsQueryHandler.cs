using ErpApp.Application.Common.Persistence;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.Contacts.Queries.ListComments;

public sealed class ListCommentsQueryHandler(IAppDbContext db) : IRequestHandler<ListCommentsQuery, CommentListDto>
{
    public async Task<CommentListDto> Handle(ListCommentsQuery request, CancellationToken cancellationToken)
    {
        var query = db.Comments.Where(x => x.OrganizationId == request.OrganizationId && x.ContactId == request.ContactId);

        var totalCount = await query.CountAsync(cancellationToken);

        var rows = await query
            .OrderByDescending(x => x.CreatedAt)
            .Skip((request.Page - 1) * request.PageSize)
            .Take(request.PageSize)
            .Select(x => new { x.Id, x.Content, x.AuthorUserId, x.CreatedAt })
            .ToListAsync(cancellationToken);

        var userIds = rows.Select(x => x.AuthorUserId).Distinct().ToList();
        var userNames = await db.Users
            .Where(x => userIds.Contains(x.Id))
            .Select(x => new { x.Id, x.FullName })
            .ToDictionaryAsync(x => x.Id, x => x.FullName, cancellationToken);

        var dtoRows = rows
            .Select(x => new CommentRowDto(x.Id, x.Content, x.AuthorUserId, userNames.GetValueOrDefault(x.AuthorUserId, "—"), x.CreatedAt))
            .ToList();

        return new CommentListDto(dtoRows, request.Page, request.PageSize, totalCount);
    }
}
