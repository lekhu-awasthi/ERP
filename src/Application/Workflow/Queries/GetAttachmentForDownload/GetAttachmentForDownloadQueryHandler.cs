using ErpApp.Application.Common.Exceptions;
using ErpApp.Application.Common.Persistence;
using ErpApp.Application.Common.Security;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.Workflow.Queries.GetAttachmentForDownload;

public sealed class GetAttachmentForDownloadQueryHandler(IAppDbContext db, ICurrentUserService currentUser)
    : IRequestHandler<GetAttachmentForDownloadQuery, AttachmentDownloadDto>
{
    public async Task<AttachmentDownloadDto> Handle(GetAttachmentForDownloadQuery request, CancellationToken cancellationToken)
    {
        var attachment = await db.Attachments
            .Where(x => x.Id == request.Id && x.OrganizationId == request.OrganizationId)
            .Select(x => new { x.FileName, x.ContentType, x.StorageKey, x.ParentType })
            .SingleOrDefaultAsync(cancellationToken)
            ?? throw new NotFoundException("Attachment not found.");

        // Phase 27a: the real permission check, now that the parent is known. Reading a file
        // attached to an Invoice requires Sales.Invoice.View -- a Member holding only Contact.View
        // must not be able to pull documents off transactions they cannot open. Deliberately after
        // the NotFound above, so a cross-tenant id stays a 404 rather than becoming an oracle.
        await GrantedPermissionReader.EnsureGrantedAsync(
            db,
            request.OrganizationId,
            currentUser.UserId,
            ParentPermissions.ViewPermissionFor(attachment.ParentType),
            cancellationToken);

        return new AttachmentDownloadDto(attachment.FileName, attachment.ContentType, attachment.StorageKey);
    }
}
