using ErpApp.Application.Common.Security;
using MediatR;

namespace ErpApp.Application.Workflow.Queries.GetAttachmentForDownload;

/// <summary>
/// Metadata-only (StorageKey, ContentType, FileName) -- returns just enough for the endpoint to
/// open the actual byte stream itself via IFileStorage (injected directly into
/// AttachmentsEndpoints, not routed through MediatR -- a raw Stream is an awkward MediatR response
/// shape, and every download still goes through this permission-checked, org-scoped query first,
/// never a raw static path -- docs/phase-18-status.md decision #1). This is the one deliberate
/// negative-permission-path proof point for cross-tenant attachment access (exit criteria #8): a
/// request for another organization's AttachmentId resolves to NotFound here (Where clause includes
/// OrganizationId), never a 200 with file bytes.
/// </summary>
public sealed record GetAttachmentForDownloadQuery(Guid OrganizationId, Guid Id)
    : IRequest<AttachmentDownloadDto>, IRequirePermission, IOrganizationScoped
{
    public string PermissionKey => PermissionKeys.ContactView;
}

public sealed record AttachmentDownloadDto(string FileName, string ContentType, string StorageKey);
