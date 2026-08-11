using ErpApp.Application.Common.Security;
using MediatR;

namespace ErpApp.Application.Contacts.Commands.UpdateContact;

public sealed record UpdateContactCommand(
    Guid OrganizationId,
    Guid Id,
    string Name,
    string? Address,
    string? Pan,
    string? Phone,
    string? Email,
    Guid? GroupId,
    decimal OpeningBalance)
    : IRequest<UpdateContactResult>, IRequirePermission, IOrganizationScoped
{
    public string PermissionKey => PermissionKeys.ContactManage;
}

public sealed record UpdateContactResult(Guid Id, string Name);
