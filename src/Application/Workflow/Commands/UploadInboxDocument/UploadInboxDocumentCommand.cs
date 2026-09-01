using ErpApp.Application.Common.Persistence;
using ErpApp.Application.Common.Security;
using ErpApp.Application.Common.Storage;
using ErpApp.Domain.Workflow;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.Workflow.Commands.UploadInboxDocument;

/// <summary>
/// Puts one scanned or photographed source document in the tenant's inbox (FR-10.3).
///
/// <para>Not Create-prefixed, matching <c>UploadAttachmentCommand</c>: "Upload" is the domain verb.
/// Deliberately not wired into <c>AuditBehavior</c> either, for that command's own reason -- the row
/// carries <c>UploadedByUserId</c>/<c>UploadedAt</c> and shows up directly in the inbox grid, so a
/// parallel audit entry would restate what the screen already says. (The <i>extraction</i> action is
/// audited, because that one sends data outward and leaves no other trace.)</para>
///
/// <para>Extraction is deliberately <b>not</b> a side effect of upload. It is a separate, explicitly
/// permissioned action (<c>ExtractInboxDocumentCommand</c>) behind a separate tenant opt-in, so
/// nothing leaves the tenant merely because somebody dragged a file onto a page.</para>
/// </summary>
public sealed record UploadInboxDocumentCommand(
    Guid OrganizationId,
    string FileName,
    long FileSizeBytes,
    string ContentType,
    Stream Content,
    string? Description = null,
    string? Label = null)
    : IRequest<InboxDocumentDto>, IRequirePermission, IOrganizationScoped
{
    public string PermissionKey => PermissionKeys.InboxDocumentManage;
}

public sealed class UploadInboxDocumentCommandValidator : AbstractValidator<UploadInboxDocumentCommand>
{
    public UploadInboxDocumentCommandValidator()
    {
        RuleFor(x => x.OrganizationId).NotEmpty();
        RuleFor(x => x.FileName).NotEmpty().MaximumLength(260);
        RuleFor(x => x.ContentType).NotEmpty().MaximumLength(150);
        RuleFor(x => x.Description).MaximumLength(500);
        RuleFor(x => x.Label).MaximumLength(60);
        RuleFor(x => x.FileSizeBytes)
            .GreaterThan(0)
            .LessThanOrEqualTo(InboxDocumentValidation.MaxSizeBytes)
            .WithMessage($"File exceeds the {InboxDocumentValidation.MaxSizeBytes / (1024 * 1024)} MB size limit.");
        RuleFor(x => x.FileName)
            .Must(InboxDocumentValidation.IsAllowedExtension)
            .WithMessage("File type is not allowed.");
    }
}

public sealed class UploadInboxDocumentCommandHandler(
    IAppDbContext db,
    IFileStorage fileStorage,
    ICurrentUserService currentUser,
    TimeProvider timeProvider)
    : IRequestHandler<UploadInboxDocumentCommand, InboxDocumentDto>
{
    public async Task<InboxDocumentDto> Handle(UploadInboxDocumentCommand request, CancellationToken cancellationToken)
    {
        var storageKey = await fileStorage.SaveAsync(request.Content, request.FileName, cancellationToken);

        var document = UploadedDocument.Create(
            request.OrganizationId,
            request.FileName,
            request.FileSizeBytes,
            request.ContentType,
            storageKey,
            request.Description,
            request.Label,
            currentUser.UserId,
            timeProvider.GetUtcNow());

        db.UploadedDocuments.Add(document);
        await db.SaveChangesAsync(cancellationToken);

        var uploaderName = await db.Users
            .Where(x => x.Id == currentUser.UserId)
            .Select(x => x.FullName)
            .SingleOrDefaultAsync(cancellationToken) ?? string.Empty;

        return InboxDocumentMapper.ToDto(document, uploaderName);
    }
}
