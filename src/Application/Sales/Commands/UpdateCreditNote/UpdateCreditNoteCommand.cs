using ErpApp.Application.Common.Security;
using ErpApp.Domain.Common;
using ErpApp.Domain.Sales;
using MediatR;

namespace ErpApp.Application.Sales.Commands.UpdateCreditNote;

public sealed record UpdateCreditNoteCommand(
    Guid OrganizationId, Guid Id, Guid ContactId, DateOnly Date, string? Reference, IReadOnlyList<CreditNoteLineInput> Lines,
    decimal DiscountPct = 0,
    // Phase 27b -- the "+ Add Terms and Conditions" block's text, pre-filled client-side from a
    // CustomTemplate and editable from there. Optional and trailing so no existing caller changes.
    string? Terms = null)
    : IRequest<UpdateCreditNoteResult>, IRequirePermission, IOrganizationScoped, ILockDateSensitive, IAuditableRequestWithId
{
    public string PermissionKey => PermissionKeys.CreditNoteEdit;
    public DocumentType AuditDocumentType => DocumentType.CreditNote;
    public Guid AuditDocumentId => Id;
}

public sealed record UpdateCreditNoteResult(Guid Id, string Code, CreditNoteStatus Status);
