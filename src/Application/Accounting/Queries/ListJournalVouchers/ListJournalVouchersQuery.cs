using ErpApp.Application.Common.Security;
using ErpApp.Domain.Accounting;
using MediatR;

namespace ErpApp.Application.Accounting.Queries.ListJournalVouchers;

public sealed record ListJournalVouchersQuery(Guid OrganizationId, JournalVoucherStatus? Status)
    : IRequest<IReadOnlyList<JournalVoucher>>, IRequirePermission, IOrganizationScoped
{
    public string PermissionKey => PermissionKeys.JournalVoucherView;
}
