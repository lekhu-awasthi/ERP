using ErpApp.Application.Common.Security;
using ErpApp.Domain.Accounting;
using MediatR;

namespace ErpApp.Application.Accounting.Commands.UpdateJournalVoucher;

public sealed record UpdateJournalVoucherCommand(
    Guid OrganizationId, Guid Id, DateOnly Date, string? Reference, IReadOnlyList<JournalVoucherLineInput> Lines)
    : IRequest<UpdateJournalVoucherResult>, IRequirePermission, IOrganizationScoped
{
    public string PermissionKey => PermissionKeys.JournalVoucherEdit;
}

public sealed record UpdateJournalVoucherResult(Guid Id, string Code, JournalVoucherStatus Status);
