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
            db, request.OrganizationId, request.ParentType, request.ParentId, cancellationToken);

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
}
