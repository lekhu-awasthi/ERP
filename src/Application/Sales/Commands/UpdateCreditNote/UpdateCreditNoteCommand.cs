using ErpApp.Application.Common.Security;
using ErpApp.Domain.Common;
using ErpApp.Domain.Sales;
using MediatR;

namespace ErpApp.Application.Sales.Commands.UpdateCreditNote;

public sealed record UpdateCreditNoteCommand(
    Guid OrganizationId, Guid Id, Guid ContactId, DateOnly Date, string? Reference, IReadOnlyList<CreditNoteLineInput> Lines,
    decimal DiscountPct = 0)
    : IRequest<UpdateCreditNoteResult>, IRequirePermission, IOrganizationScoped, ILockDateSensitive, IAuditableRequestWithId
{
    public string PermissionKey => PermissionKeys.CreditNoteEdit;
    public DocumentType AuditDocumentType => DocumentType.CreditNote;
    public Guid AuditDocumentId => Id;
}

public sealed record UpdateCreditNoteResult(Guid Id, string Code, CreditNoteStatus Status);
