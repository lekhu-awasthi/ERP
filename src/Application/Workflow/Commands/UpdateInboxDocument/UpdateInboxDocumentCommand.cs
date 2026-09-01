using ErpApp.Application.Common.Exceptions;
using ErpApp.Application.Common.Persistence;
using ErpApp.Application.Common.Security;
using ErpApp.Domain.Workflow;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.Workflow.Commands.UpdateInboxDocument;

/// <summary>
/// Edits the two free-text fields the grid shows and, separately, moves a document between the
/// Pending and Done tabs by hand.
///
/// <para>The status half exists because "Done" is not only what conversion sets: a tenant that files
/// a receipt without ever posting it needs a way off the Pending tab, and deleting the scan would
/// destroy the very record they kept it for (docs/phase-22-status.md, Decision A). Moving *back* to
/// Pending is refused once a transaction points at the document -- the aggregate owns that rule.
/// </para>
///
/// <para>The file itself is never replaced. As with <c>Attachment</c>, an in-place file swap would
/// silently change the evidence behind an already-posted transaction; re-upload and re-link
/// instead.</para>
/// </summary>
public sealed record UpdateInboxDocumentCommand(
    Guid OrganizationId,
    Guid DocumentId,
    string? Description,
    string? Label,
    UploadedDocumentStatus Status)
    : IRequest<InboxDocumentDto>, IRequirePermission, IOrganizationScoped
{
    public string PermissionKey => PermissionKeys.InboxDocumentManage;
}

public sealed class UpdateInboxDocumentCommandValidator : AbstractValidator<UpdateInboxDocumentCommand>
{
    public UpdateInboxDocumentCommandValidator()
    {
        RuleFor(x => x.OrganizationId).NotEmpty();
        RuleFor(x => x.DocumentId).NotEmpty();
        RuleFor(x => x.Description).MaximumLength(500);
        RuleFor(x => x.Label).MaximumLength(60);
        RuleFor(x => x.Status).IsInEnum();
    }
}

public sealed class UpdateInboxDocumentCommandHandler(IAppDbContext db)
    : IRequestHandler<UpdateInboxDocumentCommand, InboxDocumentDto>
{
    public async Task<InboxDocumentDto> Handle(UpdateInboxDocumentCommand request, CancellationToken cancellationToken)
    {
        var document = await db.UploadedDocuments.SingleOrDefaultAsync(
            x => x.Id == request.DocumentId && x.OrganizationId == request.OrganizationId, cancellationToken)
            ?? throw new NotFoundException("Document not found.");

        document.UpdateMetadata(request.Description, request.Label);

        // Reopen() throws for a linked document; surfaced as a 409 rather than a 500 by the
        // Application-layer exception, so the screen can say why.
        if (request.Status == UploadedDocumentStatus.Done)
        {
            document.MarkDone();
        }
        else if (document.IsLinked)
        {
            throw new ConflictException(
                "This document was converted into a transaction and cannot be moved back to Pending.");
        }
        else
        {
            document.Reopen();
        }

        await db.SaveChangesAsync(cancellationToken);

        var uploaderName = await db.Users
            .Where(x => x.Id == document.UploadedByUserId)
            .Select(x => x.FullName)
            .SingleOrDefaultAsync(cancellationToken) ?? string.Empty;

        return InboxDocumentMapper.ToDto(document, uploaderName);
    }
}
