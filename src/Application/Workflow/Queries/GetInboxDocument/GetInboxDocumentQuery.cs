using ErpApp.Application.Common.Exceptions;
using ErpApp.Application.Common.Persistence;
using ErpApp.Application.Common.Security;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.Workflow.Queries.GetInboxDocument;

/// <summary>One inbox document, including any stored extraction suggestion. Read by the conversion
/// screen so the side-by-side pane and the "these values came from extraction" banner have the same
/// source of truth the grid does.</summary>
public sealed record GetInboxDocumentQuery(Guid OrganizationId, Guid DocumentId)
    : IRequest<InboxDocumentDto>, IRequirePermission, IOrganizationScoped
{
    public string PermissionKey => PermissionKeys.InboxDocumentView;
}

public sealed class GetInboxDocumentQueryHandler(IAppDbContext db)
    : IRequestHandler<GetInboxDocumentQuery, InboxDocumentDto>
{
    public async Task<InboxDocumentDto> Handle(GetInboxDocumentQuery request, CancellationToken cancellationToken)
    {
        var document = await db.UploadedDocuments
            .Where(x => x.Id == request.DocumentId && x.OrganizationId == request.OrganizationId)
            .SingleOrDefaultAsync(cancellationToken)
            ?? throw new NotFoundException("Document not found.");

        var uploaderName = await db.Users
            .Where(x => x.Id == document.UploadedByUserId)
            .Select(x => x.FullName)
            .SingleOrDefaultAsync(cancellationToken) ?? string.Empty;

        return InboxDocumentMapper.ToDto(document, uploaderName);
    }
}
