using ErpApp.Application.Common.Filtering;
using ErpApp.Application.Common.Pagination;
using ErpApp.Application.Common.Security;
using ErpApp.Domain.Contacts;
using MediatR;

namespace ErpApp.Application.Contacts.Queries.ListContacts;

public sealed record ListContactsQuery(
    Guid OrganizationId,
    ContactType? Type,
    int Page = 1,
    int PageSize = PagingDefaults.DefaultPageSize,
    string? Search = null)
    : IRequest<PagedResult<Contact>>, IRequirePermission, IOrganizationScoped, ISearchableQuery
{
    public string PermissionKey => PermissionKeys.ContactView;
}
