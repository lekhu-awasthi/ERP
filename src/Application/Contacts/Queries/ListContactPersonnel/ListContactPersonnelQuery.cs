using ErpApp.Application.Common.Pagination;
using ErpApp.Application.Common.Security;
using MediatR;

namespace ErpApp.Application.Contacts.Queries.ListContactPersonnel;

public sealed record ListContactPersonnelQuery(
    Guid OrganizationId, Guid ContactId, int Page = 1, int PageSize = PagingDefaults.MaxPageSize)
    : IRequest<ContactPersonnelListDto>, IRequirePermission, IOrganizationScoped
{
    public string PermissionKey => PermissionKeys.ContactView;
}

public sealed record ContactPersonnelRowDto(
    Guid Id,
    string Name,
    string? Address,
    string? Code,
    string? Phone,
    Guid? GroupId,
    string? GroupName,
    string? Email,
    string? OrganizationTitle,
    DateTimeOffset CreatedAt);

public sealed record ContactPersonnelListDto(IReadOnlyList<ContactPersonnelRowDto> Rows, int Page, int PageSize, int TotalCount);
