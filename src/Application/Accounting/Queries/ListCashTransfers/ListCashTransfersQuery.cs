using ErpApp.Application.Common.Pagination;
using ErpApp.Application.Common.Security;
using ErpApp.Domain.Accounting;
using MediatR;

namespace ErpApp.Application.Accounting.Queries.ListCashTransfers;

public sealed record ListCashTransfersQuery(
    Guid OrganizationId,
    CashTransferStatus? Status,
    int Page = 1,
    int PageSize = PagingDefaults.DefaultPageSize)
    : IRequest<PagedResult<CashTransfer>>, IRequirePermission, IOrganizationScoped
{
    public string PermissionKey => PermissionKeys.CashTransferView;
}
