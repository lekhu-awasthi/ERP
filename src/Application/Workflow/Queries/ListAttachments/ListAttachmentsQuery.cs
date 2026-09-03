using ErpApp.Application.Common.Pagination;
using ErpApp.Application.Common.Security;
using ErpApp.Domain.Workflow;
using MediatR;

namespace ErpApp.Application.Workflow.Queries.ListAttachments;

public sealed record ListAttachmentsQuery(
    Guid OrganizationId, AttachmentParentType ParentType, Guid ParentId, int Page = 1, int PageSize = PagingDefaults.MaxPageSize)
    : IRequest<AttachmentListDto>, IRequirePermission, IOrganizationScoped
{
    public string PermissionKey => ParentPermissions.ViewPermissionFor(ParentType);
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
