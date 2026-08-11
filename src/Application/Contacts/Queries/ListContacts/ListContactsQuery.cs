using ErpApp.Application.Common.Security;
using ErpApp.Domain.Contacts;
using MediatR;

namespace ErpApp.Application.Contacts.Queries.ListContacts;

public sealed record ListContactsQuery(Guid OrganizationId, ContactType? Type)
    : IRequest<IReadOnlyList<Contact>>, IRequirePermission, IOrganizationScoped
{
    public string PermissionKey => PermissionKeys.ContactView;
}
