using ErpApp.Application.Common.Security;
using ErpApp.Domain.Workflow;
using MediatR;

namespace ErpApp.Application.Workflow.Commands.UploadAttachment;

/// <summary>Not Create-prefixed on purpose -- "Upload" is the domain-accurate verb, and this
/// command deliberately isn't wired into AuditBehavior (unlike Create/UpdateContactPersonnel): the
/// uploaded file already shows up directly in the Contact's own Documents tab, so a redundant
/// Activities-feed entry isn't needed (docs/phase-18-status.md decision #3).</summary>
public sealed record UploadAttachmentCommand(
    Guid OrganizationId,
    AttachmentParentType ParentType,
    Guid ParentId,
    string FileName,
    long FileSizeBytes,
    string ContentType,
    Stream Content)
    : IRequest<AttachmentResult>, IRequirePermission, IOrganizationScoped
{
    public string PermissionKey => ParentPermissions.EditPermissionFor(ParentType);
}

public sealed record AttachmentResult(
    Guid Id,
    AttachmentParentType ParentType,
    Guid ParentId,
    string FileName,
    long SizeBytes,
    string ContentType,
    Guid UploadedByUserId,
    string UploadedByName,
    DateTimeOffset UploadedAt);
