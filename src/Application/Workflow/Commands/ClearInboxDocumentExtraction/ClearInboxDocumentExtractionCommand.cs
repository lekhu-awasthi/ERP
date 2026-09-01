using ErpApp.Application.Common.Exceptions;
using ErpApp.Application.Common.Persistence;
using ErpApp.Application.Common.Security;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.Workflow.Commands.ClearInboxDocumentExtraction;

/// <summary>
/// Throws away a stored extraction suggestion, leaving the file untouched. This is the "these
/// numbers are not mine" escape hatch the honesty requirement needs (docs/phase-22-status.md,
/// Decision C): a user who does not trust what the model read must be able to make the suggestion
/// go away entirely, not merely edit around it on the conversion form.
///
/// <para>Gated on <c>InboxDocumentManage</c>, not <c>InboxDocumentExtract</c> -- discarding a
/// machine's guess is ordinary inbox housekeeping and costs nothing, whereas producing one spends
/// money and sends data outward. A Member who cannot run extraction can still clear one.</para>
/// </summary>
public sealed record ClearInboxDocumentExtractionCommand(Guid OrganizationId, Guid DocumentId)
    : IRequest<InboxDocumentDto>, IRequirePermission, IOrganizationScoped
{
    public string PermissionKey => PermissionKeys.InboxDocumentManage;
}

public sealed class ClearInboxDocumentExtractionCommandValidator
    : AbstractValidator<ClearInboxDocumentExtractionCommand>
{
    public ClearInboxDocumentExtractionCommandValidator()
    {
        RuleFor(x => x.OrganizationId).NotEmpty();
        RuleFor(x => x.DocumentId).NotEmpty();
    }
}

public sealed class ClearInboxDocumentExtractionCommandHandler(IAppDbContext db)
    : IRequestHandler<ClearInboxDocumentExtractionCommand, InboxDocumentDto>
{
    public async Task<InboxDocumentDto> Handle(
        ClearInboxDocumentExtractionCommand request, CancellationToken cancellationToken)
    {
        var document = await db.UploadedDocuments.SingleOrDefaultAsync(
            x => x.Id == request.DocumentId && x.OrganizationId == request.OrganizationId, cancellationToken)
            ?? throw new NotFoundException("Document not found.");

        document.ClearExtraction();
        await db.SaveChangesAsync(cancellationToken);

        var uploaderName = await db.Users
            .Where(x => x.Id == document.UploadedByUserId)
            .Select(x => x.FullName)
            .SingleOrDefaultAsync(cancellationToken) ?? string.Empty;

        return InboxDocumentMapper.ToDto(document, uploaderName);
    }
}
