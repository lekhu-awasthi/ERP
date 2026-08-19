using ErpApp.Application.Common.Security;
using ErpApp.Domain.Common;
using ErpApp.Domain.Sales;
using MediatR;

namespace ErpApp.Application.Sales.Commands.VoidCreditNote;

public sealed record VoidCreditNoteCommand(Guid OrganizationId, Guid Id)
    : IRequest<VoidCreditNoteResult>, IRequirePermission, IOrganizationScoped, ILockDateSensitiveDocument
{
    public string PermissionKey => PermissionKeys.CreditNoteVoid;
    public DocumentType LockDateDocumentType => DocumentType.CreditNote;
    public Guid LockDateDocumentId => Id;
}

public sealed record VoidCreditNoteResult(Guid Id, string Code, CreditNoteStatus Status, DateTimeOffset? VoidedAt);
