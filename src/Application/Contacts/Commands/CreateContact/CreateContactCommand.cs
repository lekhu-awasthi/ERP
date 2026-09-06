using ErpApp.Application.Common.Security;
using ErpApp.Domain.Common;
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
    : IRequest<CreateContactResult>, IRequirePermission, IOrganizationScoped, IAuditableRequest
{
    public string PermissionKey => PermissionKeys.ContactManage;

    // Feeds the Contact's own "Activities" sub-tab (Audit rows filtered by DocumentType=Contact,
    // DocumentId=contactId -- see Audit's own doc comment, which anticipated exactly this reuse).
    // Phase 18 is the first caller to actually wire a Contact command up to AuditBehavior.
    public DocumentType AuditDocumentType => DocumentType.Contact;
}

public sealed record CreateContactResult(Guid Id, string Code, ContactType Type, string Name);
