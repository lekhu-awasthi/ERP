using ErpApp.Application.Common.Pagination;
using ErpApp.Application.Common.Security;
using MediatR;

namespace ErpApp.Application.Accounting.Queries.ListOpeningBalanceLines;

/// <summary>Backs the Opening Balances screen's Account tab -- every Account, with its opening
/// balance if one has been set (0/0 otherwise), matching the confirmed live shape (every account
/// listed, not just the ones with a balance already entered).</summary>
public sealed record ListAccountOpeningBalancesQuery(
    Guid OrganizationId,
    int Page = 1,
    int PageSize = PagingDefaults.DefaultPageSize)
    : IRequest<PagedResult<AccountOpeningBalanceDto>>, IRequirePermission, IOrganizationScoped
{
    public string PermissionKey => PermissionKeys.OpeningBalanceView;
}

public sealed record AccountOpeningBalanceDto(
    Guid AccountId, string AccountCode, string AccountName, string RootType, string GroupName, decimal Debit, decimal Credit);
