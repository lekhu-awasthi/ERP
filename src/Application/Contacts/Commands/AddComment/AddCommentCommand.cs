using ErpApp.Application.Common.Security;
using MediatR;

namespace ErpApp.Application.Contacts.Commands.AddComment;

public sealed record AddCommentCommand(Guid OrganizationId, Guid ContactId, string Content)
    : IRequest<CommentResult>, IRequirePermission, IOrganizationScoped
{
    public string PermissionKey => PermissionKeys.ContactManage;
}

public sealed record CommentResult(Guid Id, Guid ContactId, string Content, Guid AuthorUserId, string AuthorName, DateTimeOffset CreatedAt);
