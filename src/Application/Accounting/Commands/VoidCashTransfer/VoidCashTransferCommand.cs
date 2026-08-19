using ErpApp.Application.Common.Security;
using ErpApp.Domain.Accounting;
using ErpApp.Domain.Common;
using MediatR;

namespace ErpApp.Application.Accounting.Commands.VoidCashTransfer;

public sealed record VoidCashTransferCommand(Guid OrganizationId, Guid Id)
    : IRequest<VoidCashTransferResult>, IRequirePermission, IOrganizationScoped, ILockDateSensitiveDocument
{
    public string PermissionKey => PermissionKeys.CashTransferVoid;
    public DocumentType LockDateDocumentType => DocumentType.CashTransfer;
    public Guid LockDateDocumentId => Id;
}

public sealed record VoidCashTransferResult(Guid Id, string Code, CashTransferStatus Status, DateTimeOffset? VoidedAt);
