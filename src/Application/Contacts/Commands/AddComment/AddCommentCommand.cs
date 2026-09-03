using ErpApp.Application.Common.Security;
using ErpApp.Domain.Workflow;
using MediatR;

namespace ErpApp.Application.Contacts.Commands.AddComment;

/// <summary>
/// Phase 27a made this polymorphic (see Comment's own doc comment for the evidence). The key now
/// comes from the parent via <see cref="ParentPermissions"/> rather than being hardcoded to
/// ContactManage: commenting on an Invoice requires Sales.Invoice.Edit, and a Member with no
/// Contact grant at all can still comment on documents they may edit.
/// </summary>
public sealed record AddCommentCommand(Guid OrganizationId, CommentParentType ParentType, Guid ParentId, string Content)
    : IRequest<CommentResult>, IRequirePermission, IOrganizationScoped
{
    public string PermissionKey => ParentPermissions.EditPermissionFor(ParentType);
}

public sealed record CommentResult(
    Guid Id,
    CommentParentType ParentType,
    Guid ParentId,
    string Content,
    Guid AuthorUserId,
    string AuthorName,
    DateTimeOffset CreatedAt);
