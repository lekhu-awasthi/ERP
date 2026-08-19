using ErpApp.Application.Common.Security;
using ErpApp.Domain.Accounting;
using MediatR;

namespace ErpApp.Application.Accounting.Commands.UpdateCashTransfer;

public sealed record UpdateCashTransferCommand(
    Guid OrganizationId, Guid Id, DateOnly Date, string? Reference, Guid FromAccountId, IReadOnlyList<CashTransferLineInput> Lines)
    : IRequest<UpdateCashTransferResult>, IRequirePermission, IOrganizationScoped, ILockDateSensitive
{
    public string PermissionKey => PermissionKeys.CashTransferEdit;
}

public sealed record UpdateCashTransferResult(Guid Id, string Code, CashTransferStatus Status);
