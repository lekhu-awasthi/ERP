using ErpApp.Application.Common.Security;
using ErpApp.Domain.Accounting;
using MediatR;

namespace ErpApp.Application.Accounting.Commands.ApproveJournalVoucher;

public sealed record ApproveJournalVoucherCommand(Guid OrganizationId, Guid Id)
    : IRequest<ApproveJournalVoucherResult>, IRequirePermission, IOrganizationScoped
{
    public string PermissionKey => PermissionKeys.JournalVoucherApprove;
}

public sealed record ApproveJournalVoucherResult(Guid Id, string Code, JournalVoucherStatus Status, DateTimeOffset? ApprovedAt);
