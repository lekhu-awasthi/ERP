using ErpApp.Application.Common.Security;
using ErpApp.Domain.Accounting;
using ErpApp.Domain.Common;
using MediatR;

namespace ErpApp.Application.Accounting.Commands.UpdateCashTransfer;

public sealed record UpdateCashTransferCommand(
    Guid OrganizationId, Guid Id, DateOnly Date, string? Reference, Guid FromAccountId, IReadOnlyList<CashTransferLineInput> Lines)
    : IRequest<UpdateCashTransferResult>, IRequirePermission, IOrganizationScoped, ILockDateSensitive, IAuditableRequestWithId
{
    public string PermissionKey => PermissionKeys.CashTransferEdit;
    public DocumentType AuditDocumentType => DocumentType.CashTransfer;
    public Guid AuditDocumentId => Id;
}

public sealed record UpdateCashTransferResult(Guid Id, string Code, CashTransferStatus Status);
