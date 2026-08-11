using ErpApp.Application.Common.Security;
using ErpApp.Domain.Accounting;
using MediatR;

namespace ErpApp.Application.Accounting.Commands.CreateJournalVoucher;

public sealed record CreateJournalVoucherCommand(
    Guid OrganizationId, DateOnly Date, string? Reference, IReadOnlyList<JournalVoucherLineInput> Lines)
    : IRequest<CreateJournalVoucherResult>, IRequirePermission, IOrganizationScoped
{
    public string PermissionKey => PermissionKeys.JournalVoucherCreate;
}

public sealed record CreateJournalVoucherResult(Guid Id, string Code, JournalVoucherStatus Status);
