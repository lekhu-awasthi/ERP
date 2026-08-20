using ErpApp.Application.Common.Security;
using ErpApp.Domain.Common;
using ErpApp.Domain.Purchasing;
using MediatR;

namespace ErpApp.Application.Purchasing.Commands.UpdateDebitNote;

public sealed record UpdateDebitNoteCommand(
    Guid OrganizationId, Guid Id, Guid ContactId, DateOnly Date, string? Reference, Guid? TdsTypeId,
    IReadOnlyList<DebitNoteLineInput> Lines, decimal DiscountPct = 0)
    : IRequest<UpdateDebitNoteResult>, IRequirePermission, IOrganizationScoped, ILockDateSensitive, IAuditableRequestWithId
{
    public string PermissionKey => PermissionKeys.DebitNoteEdit;
    public DocumentType AuditDocumentType => DocumentType.DebitNote;
    public Guid AuditDocumentId => Id;
}

public sealed record UpdateDebitNoteResult(Guid Id, string Code, DebitNoteStatus Status);
