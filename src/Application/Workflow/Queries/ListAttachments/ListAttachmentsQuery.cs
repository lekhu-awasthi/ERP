using ErpApp.Application.Common.Locations;
using ErpApp.Application.Common.Pagination;
using ErpApp.Application.Common.Security;
using ErpApp.Domain.Workflow;
using MediatR;

namespace ErpApp.Application.Workflow.Queries.ListAttachments;

public sealed record ListAttachmentsQuery(
    Guid OrganizationId, AttachmentParentType ParentType, Guid ParentId, int Page = 1, int PageSize = PagingDefaults.MaxPageSize)
    : IRequest<AttachmentListDto>, IRequirePermission, IOrganizationScoped, ILocationScopedDocument
{
    public string PermissionKey => ParentPermissions.ViewPermissionFor(ParentType);

    /// <summary>Phase 32b -- when the parent is a document, the key above is that document&#39;s own, so
    /// a location-scoped caller must hold it at the parent&#39;s location. When the parent is a Contact the
    /// key is not location-scopable and the check never runs.</summary>
    public Guid LocationDocumentId => ParentId;
}

public sealed record AttachmentRowDto(
    Guid Id,
    string FileName,
    long SizeBytes,
    string ContentType,
    Guid UploadedByUserId,
    string UploadedByName,
    DateTimeOffset UploadedAt);

public sealed record AttachmentListDto(IReadOnlyList<AttachmentRowDto> Rows, int Page, int PageSize, int TotalCount);
