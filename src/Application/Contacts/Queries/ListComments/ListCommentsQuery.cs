using ErpApp.Application.Common.Pagination;
using ErpApp.Application.Common.Security;
using ErpApp.Domain.Workflow;
using MediatR;

namespace ErpApp.Application.Contacts.Queries.ListComments;

/// <summary>Phase 27a: parent-scoped rather than Contact-scoped, with the View key derived from that
/// parent -- see AddCommentCommand.</summary>
public sealed record ListCommentsQuery(
    Guid OrganizationId,
    CommentParentType ParentType,
    Guid ParentId,
    int Page = 1,
    int PageSize = PagingDefaults.MaxPageSize)
    : IRequest<CommentListDto>, IRequirePermission, IOrganizationScoped
{
    public string PermissionKey => ParentPermissions.ViewPermissionFor(ParentType);
}

public sealed record CommentRowDto(Guid Id, string Content, Guid AuthorUserId, string AuthorName, DateTimeOffset CreatedAt);

public sealed record CommentListDto(IReadOnlyList<CommentRowDto> Rows, int Page, int PageSize, int TotalCount);
