using ErpApp.Application.Common.Security;
using ErpApp.Application.Contacts.Commands.CreateContactPersonnel;
using ErpApp.Domain.Common;
using MediatR;

namespace ErpApp.Application.Contacts.Commands.UpdateContactPersonnel;

public sealed record UpdateContactPersonnelCommand(
    Guid OrganizationId,
    Guid ContactId,
    Guid Id,
    string Name,
    string? Address,
    string? Code,
    string? Phone,
    Guid? GroupId,
    string? Email,
    string? OrganizationTitle)
    : IRequest<ContactPersonnelResult>, IRequirePermission, IOrganizationScoped, IAuditableRequestWithId
{
    public string PermissionKey => PermissionKeys.ContactManage;

    public DocumentType AuditDocumentType => DocumentType.Contact;

    public Guid AuditDocumentId => ContactId;
}
