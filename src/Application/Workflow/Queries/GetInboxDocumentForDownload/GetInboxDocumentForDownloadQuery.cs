using ErpApp.Application.Common.Exceptions;
using ErpApp.Application.Common.Persistence;
using ErpApp.Application.Common.Security;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.Workflow.Queries.GetInboxDocumentForDownload;

/// <summary>
/// Metadata-only, exactly like <c>GetAttachmentForDownloadQuery</c> -- enough for the endpoint to
/// open the byte stream itself via <c>IFileStorage</c>, which is an awkward MediatR response shape.
/// <c>IFileStorage</c> deliberately exposes no public URL, so this permission-checked, org-scoped
/// query is the only route to an inbox scan's bytes.
///
/// <para>This is the handler-level proof point for cross-tenant isolation: another organization's
/// DocumentId resolves to NotFound here (the Where clause carries OrganizationId -- there is no EF
/// global query filter in this codebase), never a 200 with file bytes.</para>
/// </summary>
public sealed record GetInboxDocumentForDownloadQuery(Guid OrganizationId, Guid DocumentId)
    : IRequest<InboxDocumentDownloadDto>, IRequirePermission, IOrganizationScoped
{
    public string PermissionKey => PermissionKeys.InboxDocumentView;
}

public sealed record InboxDocumentDownloadDto(string FileName, string ContentType, string StorageKey);

public sealed class GetInboxDocumentForDownloadQueryHandler(IAppDbContext db)
    : IRequestHandler<GetInboxDocumentForDownloadQuery, InboxDocumentDownloadDto>
{
    public async Task<InboxDocumentDownloadDto> Handle(
        GetInboxDocumentForDownloadQuery request, CancellationToken cancellationToken)
    {
        var document = await db.UploadedDocuments
            .Where(x => x.Id == request.DocumentId && x.OrganizationId == request.OrganizationId)
            .Select(x => new { x.FileName, x.ContentType, x.StorageKey })
            .SingleOrDefaultAsync(cancellationToken)
            ?? throw new NotFoundException("Document not found.");

        return new InboxDocumentDownloadDto(document.FileName, document.ContentType, document.StorageKey);
    }
}
