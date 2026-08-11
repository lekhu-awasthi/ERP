using ErpApp.Application.Common.Security;
using ErpApp.Domain.Contacts;
using MediatR;

namespace ErpApp.Application.Contacts.Queries.GetContact;

public sealed record GetContactQuery(Guid OrganizationId, Guid Id)
    : IRequest<Contact>, IRequirePermission, IOrganizationScoped
{
    public string PermissionKey => PermissionKeys.ContactView;
}
