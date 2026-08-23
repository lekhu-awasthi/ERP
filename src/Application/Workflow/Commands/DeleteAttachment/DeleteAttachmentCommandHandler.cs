using ErpApp.Application.Common.Exceptions;
using ErpApp.Application.Common.Persistence;
using ErpApp.Application.Common.Storage;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.Workflow.Commands.DeleteAttachment;

public sealed class DeleteAttachmentCommandHandler(IAppDbContext db, IFileStorage fileStorage)
    : IRequestHandler<DeleteAttachmentCommand, Unit>
{
    public async Task<Unit> Handle(DeleteAttachmentCommand request, CancellationToken cancellationToken)
    {
        var attachment = await db.Attachments.SingleOrDefaultAsync(
            x => x.Id == request.Id && x.OrganizationId == request.OrganizationId, cancellationToken)
            ?? throw new NotFoundException("Attachment not found.");

        var storageKey = attachment.StorageKey;

        db.Attachments.Remove(attachment);
        await db.SaveChangesAsync(cancellationToken);

        // Deleted only after the DB row is safely committed -- a failure here leaves an orphaned
        // file on disk (safe, cleanable) rather than an orphaned DB row pointing at a file that no
        // longer exists (unsafe -- a later download would 404/500 for no visible reason).
        await fileStorage.DeleteAsync(storageKey, cancellationToken);

        return Unit.Value;
    }
}
