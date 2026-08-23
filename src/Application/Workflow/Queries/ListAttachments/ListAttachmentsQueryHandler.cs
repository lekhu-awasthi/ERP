using ErpApp.Application.Common.Persistence;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.Workflow.Queries.ListAttachments;

public sealed class ListAttachmentsQueryHandler(IAppDbContext db) : IRequestHandler<ListAttachmentsQuery, AttachmentListDto>
{
    public async Task<AttachmentListDto> Handle(ListAttachmentsQuery request, CancellationToken cancellationToken)
    {
        var query = db.Attachments.Where(x =>
            x.OrganizationId == request.OrganizationId && x.ParentType == request.ParentType && x.ParentId == request.ParentId);

        var totalCount = await query.CountAsync(cancellationToken);

        var rows = await query
            .OrderByDescending(x => x.UploadedAt)
            .Skip((request.Page - 1) * request.PageSize)
            .Take(request.PageSize)
            .Select(x => new { x.Id, x.FileName, x.SizeBytes, x.ContentType, x.UploadedByUserId, x.UploadedAt })
            .ToListAsync(cancellationToken);

        var userIds = rows.Select(x => x.UploadedByUserId).Distinct().ToList();
        var userNames = await db.Users
            .Where(x => userIds.Contains(x.Id))
            .Select(x => new { x.Id, x.FullName })
            .ToDictionaryAsync(x => x.Id, x => x.FullName, cancellationToken);

        var dtoRows = rows.Select(x => new AttachmentRowDto(
            x.Id, x.FileName, x.SizeBytes, x.ContentType, x.UploadedByUserId,
            userNames.GetValueOrDefault(x.UploadedByUserId, "—"), x.UploadedAt))
            .ToList();

        return new AttachmentListDto(dtoRows, request.Page, request.PageSize, totalCount);
    }
}
