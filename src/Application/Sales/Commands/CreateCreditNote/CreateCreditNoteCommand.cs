using ErpApp.Application.Common.Security;
using ErpApp.Domain.Common;
using ErpApp.Domain.Sales;
using MediatR;

namespace ErpApp.Application.Sales.Commands.CreateCreditNote;

public sealed record CreateCreditNoteCommand(
    Guid OrganizationId,
    Guid ContactId,
    DateOnly Date,
    string? Reference,
    IReadOnlyList<CreditNoteLineInput> Lines,
    DocumentType? ReferrerType = null,
    Guid? ReferrerId = null,
    decimal DiscountPct = 0,
    // Phase 27b -- the "+ Add Terms and Conditions" block's text, pre-filled client-side from a
    // CustomTemplate and editable from there. Optional and trailing so no existing caller changes.
    string? Terms = null)
    : IRequest<CreateCreditNoteResult>, IRequirePermission, IOrganizationScoped, ILockDateSensitive, IAuditableRequest
{
    public string PermissionKey => PermissionKeys.CreditNoteCreate;
    public DocumentType AuditDocumentType => DocumentType.CreditNote;
}

public sealed record CreateCreditNoteResult(Guid Id, string Code, CreditNoteStatus Status);
