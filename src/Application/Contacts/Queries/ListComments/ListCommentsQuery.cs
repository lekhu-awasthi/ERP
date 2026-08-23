using ErpApp.Application.Common.Pagination;
using ErpApp.Application.Common.Security;
using MediatR;

namespace ErpApp.Application.Contacts.Queries.ListComments;

public sealed record ListCommentsQuery(Guid OrganizationId, Guid ContactId, int Page = 1, int PageSize = PagingDefaults.MaxPageSize)
    : IRequest<CommentListDto>, IRequirePermission, IOrganizationScoped
{
    public string PermissionKey => PermissionKeys.ContactView;
}

public sealed record CommentRowDto(Guid Id, string Content, Guid AuthorUserId, string AuthorName, DateTimeOffset CreatedAt);

public sealed record CommentListDto(IReadOnlyList<CommentRowDto> Rows, int Page, int PageSize, int TotalCount);
