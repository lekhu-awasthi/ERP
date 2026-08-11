using ErpApp.Application.Common.Security;
using ErpApp.Domain.Accounting;
using MediatR;

namespace ErpApp.Application.Accounting.Queries.ListAccounts;

public sealed record ListAccountsQuery(Guid OrganizationId, AccountRootType? RootType)
    : IRequest<IReadOnlyList<Account>>, IRequirePermission, IOrganizationScoped
{
    public string PermissionKey => PermissionKeys.AccountView;
}
