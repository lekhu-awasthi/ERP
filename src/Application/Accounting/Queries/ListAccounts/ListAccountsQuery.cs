using ErpApp.Application.Common.Filtering;
using ErpApp.Application.Common.Pagination;
using ErpApp.Application.Common.Security;
using ErpApp.Domain.Accounting;
using MediatR;

namespace ErpApp.Application.Accounting.Queries.ListAccounts;

public sealed record ListAccountsQuery(
    Guid OrganizationId,
    AccountRootType? RootType,
    int Page = 1,
    int PageSize = PagingDefaults.DefaultPageSize,
    string? Search = null)
    : IRequest<PagedResult<Account>>, IRequirePermission, IOrganizationScoped, ISearchableQuery
{
    public string PermissionKey => PermissionKeys.AccountView;
}
