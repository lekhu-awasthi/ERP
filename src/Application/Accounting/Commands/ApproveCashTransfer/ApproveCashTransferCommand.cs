using ErpApp.Application.Common.Security;
using ErpApp.Domain.Accounting;
using ErpApp.Domain.Common;
using MediatR;

namespace ErpApp.Application.Accounting.Commands.ApproveCashTransfer;

public sealed record ApproveCashTransferCommand(Guid OrganizationId, Guid Id)
    : IRequest<ApproveCashTransferResult>, IRequirePermission, IOrganizationScoped, ILockDateSensitiveDocument
{
    public string PermissionKey => PermissionKeys.CashTransferApprove;
    public DocumentType LockDateDocumentType => DocumentType.CashTransfer;
    public Guid LockDateDocumentId => Id;
}

public sealed record ApproveCashTransferResult(Guid Id, string Code, CashTransferStatus Status, DateTimeOffset? ApprovedAt);
