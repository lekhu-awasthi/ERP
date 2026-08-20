using ErpApp.Application.Common.Security;
using ErpApp.Domain.Accounting;
using ErpApp.Domain.Common;
using MediatR;

namespace ErpApp.Application.Accounting.Commands.CreateCashTransfer;

public sealed record CreateCashTransferCommand(
    Guid OrganizationId, DateOnly Date, string? Reference, Guid FromAccountId, IReadOnlyList<CashTransferLineInput> Lines)
    : IRequest<CreateCashTransferResult>, IRequirePermission, IOrganizationScoped, ILockDateSensitive, IAuditableRequest
{
    public string PermissionKey => PermissionKeys.CashTransferCreate;
    public DocumentType AuditDocumentType => DocumentType.CashTransfer;
}

public sealed record CreateCashTransferResult(Guid Id, string Code, CashTransferStatus Status);
