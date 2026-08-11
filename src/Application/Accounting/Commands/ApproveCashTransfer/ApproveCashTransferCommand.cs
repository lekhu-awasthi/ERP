using ErpApp.Application.Common.Security;
using ErpApp.Domain.Accounting;
using MediatR;

namespace ErpApp.Application.Accounting.Commands.ApproveCashTransfer;

public sealed record ApproveCashTransferCommand(Guid OrganizationId, Guid Id)
    : IRequest<ApproveCashTransferResult>, IRequirePermission, IOrganizationScoped
{
    public string PermissionKey => PermissionKeys.CashTransferApprove;
}

public sealed record ApproveCashTransferResult(Guid Id, string Code, CashTransferStatus Status, DateTimeOffset? ApprovedAt);
