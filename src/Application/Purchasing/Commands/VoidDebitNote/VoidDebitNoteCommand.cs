using ErpApp.Application.Common.Security;
using ErpApp.Domain.Common;
using ErpApp.Domain.Purchasing;
using MediatR;

namespace ErpApp.Application.Purchasing.Commands.VoidDebitNote;

public sealed record VoidDebitNoteCommand(Guid OrganizationId, Guid Id)
    : IRequest<VoidDebitNoteResult>, IRequirePermission, IOrganizationScoped, ILockDateSensitiveDocument
{
    public string PermissionKey => PermissionKeys.DebitNoteVoid;
    public DocumentType LockDateDocumentType => DocumentType.DebitNote;
    public Guid LockDateDocumentId => Id;
}

public sealed record VoidDebitNoteResult(Guid Id, string Code, DebitNoteStatus Status, DateTimeOffset? VoidedAt);
