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
    decimal OpeningBalance,
    // Phase 31 (credit control). Trailing and optional so every existing caller and test compiles
    // unchanged -- but note phase-27b's Terms lesson: the Api's own request record has to carry
    // them too, or they bind to null forever and every test still passes.
    //
    // A Lead silently drops all three (Contact.Create enforces it) rather than failing validation:
    // the live form simply does not render the block for a Lead, so a client sending them is
    // sending noise, not making an error.
    decimal CreditLimit = 0m,
    Guid? CreditTermId = null,
    bool AcceptsReverseTransactions = false)
    : IRequest<UpdateContactResult>, IRequirePermission, IOrganizationScoped, IAuditableRequestWithId
{
    public string PermissionKey => PermissionKeys.ContactManage;

    public DocumentType AuditDocumentType => DocumentType.Contact;

    public Guid AuditDocumentId => Id;
}

public sealed record UpdateContactResult(Guid Id, string Name);
