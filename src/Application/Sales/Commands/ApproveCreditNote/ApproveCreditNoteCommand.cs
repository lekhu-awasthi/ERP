using ErpApp.Application.Common.Security;
using ErpApp.Domain.Common;
using ErpApp.Domain.Sales;
using MediatR;

namespace ErpApp.Application.Sales.Commands.ApproveCreditNote;

public sealed record ApproveCreditNoteCommand(Guid OrganizationId, Guid Id)
    : IRequest<ApproveCreditNoteResult>, IRequirePermission, IOrganizationScoped, ILockDateSensitiveDocument
{
    public string PermissionKey => PermissionKeys.CreditNoteApprove;
    public DocumentType LockDateDocumentType => DocumentType.CreditNote;
    public Guid LockDateDocumentId => Id;
}

public sealed record ApproveCreditNoteResult(Guid Id, string Code, CreditNoteStatus Status, DateTimeOffset? ApprovedAt);
