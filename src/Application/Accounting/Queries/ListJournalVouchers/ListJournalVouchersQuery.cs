using ErpApp.Application.Common.Pagination;
using ErpApp.Application.Common.Security;
using ErpApp.Domain.Accounting;
using MediatR;

namespace ErpApp.Application.Accounting.Queries.ListJournalVouchers;

public sealed record ListJournalVouchersQuery(
    Guid OrganizationId,
    JournalVoucherStatus? Status,
    int Page = 1,
    int PageSize = PagingDefaults.DefaultPageSize)
    : IRequest<PagedResult<JournalVoucher>>, IRequirePermission, IOrganizationScoped
{
    public string PermissionKey => PermissionKeys.JournalVoucherView;
}
