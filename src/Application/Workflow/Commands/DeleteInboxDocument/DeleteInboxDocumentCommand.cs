using ErpApp.Application.Common.Exceptions;
using ErpApp.Application.Common.Persistence;
using ErpApp.Application.Common.Security;
using ErpApp.Application.Common.Storage;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.Workflow.Commands.DeleteInboxDocument;

/// <summary>
/// Removes an inbox document and its file. <b>Refused once a transaction points at it</b> --
/// deleting the scan behind a posted Purchase Bill would leave that bill's "Source document" panel
/// pointing at nothing, and in Nepal the scan is often the very thing the tenant is required to
/// retain (docs/phase-22-status.md, Decision A / grounded finding #10).
///
/// <para>An inbox scan is therefore <b>never</b> swept by retention, unlike a job artifact
/// (phase-21b's Decision E): a job artifact is a derived convenience the tenant can regenerate, and
/// an inbox scan is the primary evidence. There is no <c>SweepAsync</c> here and there deliberately
/// is not going to be one.</para>
///
/// <para>Ordering follows <c>DeleteAttachmentCommandHandler</c> exactly: commit the row removal
/// first, delete the blob second. A crash between the two leaves an orphaned file (harmless,
/// cleanable) rather than a row pointing at a file that no longer exists.</para>
/// </summary>
public sealed record DeleteInboxDocumentCommand(Guid OrganizationId, Guid DocumentId)
    : IRequest<Unit>, IRequirePermission, IOrganizationScoped
{
    public string PermissionKey => PermissionKeys.InboxDocumentManage;
}

public sealed class DeleteInboxDocumentCommandValidator : AbstractValidator<DeleteInboxDocumentCommand>
{
    public DeleteInboxDocumentCommandValidator()
    {
        RuleFor(x => x.OrganizationId).NotEmpty();
        RuleFor(x => x.DocumentId).NotEmpty();
    }
}

public sealed class DeleteInboxDocumentCommandHandler(IAppDbContext db, IFileStorage fileStorage)
    : IRequestHandler<DeleteInboxDocumentCommand, Unit>
{
    public async Task<Unit> Handle(DeleteInboxDocumentCommand request, CancellationToken cancellationToken)
    {
        var document = await db.UploadedDocuments.SingleOrDefaultAsync(
            x => x.Id == request.DocumentId && x.OrganizationId == request.OrganizationId, cancellationToken)
            ?? throw new NotFoundException("Document not found.");

        if (document.IsLinked)
        {
            throw new ConflictException(
                "This document is the source of a transaction and cannot be deleted. Void or delete that transaction first.");
        }

        var storageKey = document.StorageKey;

        db.UploadedDocuments.Remove(document);
        await db.SaveChangesAsync(cancellationToken);

        await fileStorage.DeleteAsync(storageKey, cancellationToken);

        return Unit.Value;
    }
}
