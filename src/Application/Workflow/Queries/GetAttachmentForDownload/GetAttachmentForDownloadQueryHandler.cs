using ErpApp.Application.Common.Exceptions;
using ErpApp.Application.Common.Persistence;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.Workflow.Queries.GetAttachmentForDownload;

public sealed class GetAttachmentForDownloadQueryHandler(IAppDbContext db)
    : IRequestHandler<GetAttachmentForDownloadQuery, AttachmentDownloadDto>
{
    public async Task<AttachmentDownloadDto> Handle(GetAttachmentForDownloadQuery request, CancellationToken cancellationToken)
    {
        var attachment = await db.Attachments
            .Where(x => x.Id == request.Id && x.OrganizationId == request.OrganizationId)
            .Select(x => new { x.FileName, x.ContentType, x.StorageKey })
            .SingleOrDefaultAsync(cancellationToken)
            ?? throw new NotFoundException("Attachment not found.");

        return new AttachmentDownloadDto(attachment.FileName, attachment.ContentType, attachment.StorageKey);
    }
}
