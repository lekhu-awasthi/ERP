using ErpApp.Application.Common.Security;
using ErpApp.Domain.Common;
using MediatR;

namespace ErpApp.Application.Contacts.Commands.CreateContactPersonnel;

/// <summary>Create-prefixed (not "Add") specifically so AuditBehavior's prefix-based
/// ResolveAction() picks this command up automatically, feeding the parent Contact's own
/// Activities sub-tab (docs/phase-18-status.md decision #3). Implements the *WithId* variant
/// (not the base IAuditableRequest, which would resolve DocumentId by reflecting the response's
/// own "Id" -- this new Personnel row's Id, not what we want) because the parent ContactId to
/// audit against is already known on the request itself, unlike a brand-new Contact's own Id.</summary>
public sealed record CreateContactPersonnelCommand(
    Guid OrganizationId,
    Guid ContactId,
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

public sealed record ContactPersonnelResult(
    Guid Id,
    Guid ContactId,
    string Name,
    string? Address,
    string? Code,
    string? Phone,
    Guid? GroupId,
    string? Email,
    string? OrganizationTitle);
