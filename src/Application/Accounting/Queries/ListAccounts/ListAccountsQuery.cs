using ErpApp.Application.Common.Pagination;
using ErpApp.Application.Common.Security;
using ErpApp.Domain.Accounting;
using MediatR;

namespace ErpApp.Application.Accounting.Queries.ListAccounts;

public sealed record ListAccountsQuery(
    Guid OrganizationId,
    AccountRootType? RootType,
    int Page = 1,
    int PageSize = PagingDefaults.DefaultPageSize)
    : IRequest<PagedResult<Account>>, IRequirePermission, IOrganizationScoped
{
    public string PermissionKey => PermissionKeys.AccountView;
}
