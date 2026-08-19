using ErpApp.Application.Common.Security;
using ErpApp.Domain.Common;
using ErpApp.Domain.Purchasing;
using MediatR;

namespace ErpApp.Application.Purchasing.Commands.ApproveDebitNote;

public sealed record ApproveDebitNoteCommand(Guid OrganizationId, Guid Id)
    : IRequest<ApproveDebitNoteResult>, IRequirePermission, IOrganizationScoped, ILockDateSensitiveDocument
{
    public string PermissionKey => PermissionKeys.DebitNoteApprove;
    public DocumentType LockDateDocumentType => DocumentType.DebitNote;
    public Guid LockDateDocumentId => Id;
}

public sealed record ApproveDebitNoteResult(Guid Id, string Code, DebitNoteStatus Status, DateTimeOffset? ApprovedAt);
