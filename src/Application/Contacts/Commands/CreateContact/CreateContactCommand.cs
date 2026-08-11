using ErpApp.Application.Common.Security;
using ErpApp.Domain.Contacts;
using MediatR;

namespace ErpApp.Application.Contacts.Commands.CreateContact;

public sealed record CreateContactCommand(
    Guid OrganizationId,
    ContactType Type,
    string Name,
    string? Address,
    string? Pan,
    string? Phone,
    string? Email,
    Guid? GroupId,
    decimal OpeningBalance)
    : IRequest<CreateContactResult>, IRequirePermission, IOrganizationScoped
{
    public string PermissionKey => PermissionKeys.ContactManage;
}

public sealed record CreateContactResult(Guid Id, string Code, ContactType Type, string Name);
