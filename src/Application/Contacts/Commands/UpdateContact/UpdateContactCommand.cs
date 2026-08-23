using ErpApp.Application.Common.Security;
using ErpApp.Domain.Common;
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
    : IRequest<UpdateContactResult>, IRequirePermission, IOrganizationScoped, IAuditableRequestWithId
{
    public string PermissionKey => PermissionKeys.ContactManage;

    public DocumentType AuditDocumentType => DocumentType.Contact;

    public Guid AuditDocumentId => Id;
}

public sealed record UpdateContactResult(Guid Id, string Name);
