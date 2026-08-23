using ErpApp.Application.Common.Persistence;
using ErpApp.Application.Common.Security;
using ErpApp.Application.Common.Storage;
using ErpApp.Domain.Workflow;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.Workflow.Commands.UploadAttachment;

public sealed class UploadAttachmentCommandHandler(IAppDbContext db, IFileStorage fileStorage, ICurrentUserService currentUser)
    : IRequestHandler<UploadAttachmentCommand, AttachmentResult>
{
    public async Task<AttachmentResult> Handle(UploadAttachmentCommand request, CancellationToken cancellationToken)
    {
        await WorkflowValidation.EnsureParentExistsAsync(
            db, request.OrganizationId, MapParentType(request.ParentType), request.ParentId, cancellationToken);

        var storageKey = await fileStorage.SaveAsync(request.Content, request.FileName, cancellationToken);

        var attachment = Attachment.Create(
            request.OrganizationId, request.ParentType, request.ParentId, request.FileName, request.FileSizeBytes,
            request.ContentType, storageKey, currentUser.UserId);

        db.Attachments.Add(attachment);
        await db.SaveChangesAsync(cancellationToken);

        var uploaderName = await db.Users
            .Where(x => x.Id == currentUser.UserId)
            .Select(x => x.FullName)
            .SingleAsync(cancellationToken);

        return new AttachmentResult(
            attachment.Id, attachment.ParentType, attachment.ParentId, attachment.FileName, attachment.SizeBytes,
            attachment.ContentType, attachment.UploadedByUserId, uploaderName, attachment.UploadedAt);
    }

    // AttachmentParentType currently only has Contact (see its own doc comment) -- this maps into
    // WorkflowValidation's existing TaskParentType-shaped existence check rather than duplicating
    // it, since both enums resolve identically today (Contact -> a Contacts row). A second
    // AttachmentParentType value would need its own branch here, not a shared switch with
    // TaskParentType -- the two enums are deliberately not unified (AttachmentParentType's own doc
    // comment).
    private static TaskParentType MapParentType(AttachmentParentType parentType) => parentType switch
    {
        AttachmentParentType.Contact => TaskParentType.Contact,
        _ => throw new ArgumentOutOfRangeException(nameof(parentType), parentType, null),
    };
}
