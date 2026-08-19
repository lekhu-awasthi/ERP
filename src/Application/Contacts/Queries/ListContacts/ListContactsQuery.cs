using ErpApp.Application.Common.Pagination;
using ErpApp.Application.Common.Security;
using ErpApp.Domain.Contacts;
using MediatR;

namespace ErpApp.Application.Contacts.Queries.ListContacts;

public sealed record ListContactsQuery(
    Guid OrganizationId,
    ContactType? Type,
    int Page = 1,
    int PageSize = PagingDefaults.DefaultPageSize)
    : IRequest<PagedResult<Contact>>, IRequirePermission, IOrganizationScoped
{
    public string PermissionKey => PermissionKeys.ContactView;
}
