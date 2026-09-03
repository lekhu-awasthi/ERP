using ErpApp.Application.Common.Security;
using MediatR;

namespace ErpApp.Application.Workflow.Commands.DeleteAttachment;

/// <summary>
/// Addressed by attachment id alone, so <see cref="PermissionKey"/> is the blanket
/// <see cref="PermissionKeys.AttachmentAccess"/> -- the parent whose Edit grant actually decides
/// this is a column on the row the handler is about to read, and PermissionKey is evaluated before
/// the handler runs. The real gate is in DeleteAttachmentCommandHandler; see
/// PermissionKeys.AttachmentAccess for the full contract.
/// </summary>
public sealed record DeleteAttachmentCommand(Guid OrganizationId, Guid Id)
    : IRequest<Unit>, IRequirePermission, IOrganizationScoped
{
    public string PermissionKey => PermissionKeys.AttachmentAccess;
}
