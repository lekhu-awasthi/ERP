using ErpApp.Application.Common.Security;
using ErpApp.Domain.Accounting;
using MediatR;

namespace ErpApp.Application.Accounting.Queries.GetAccount;

public sealed record GetAccountQuery(Guid OrganizationId, Guid Id)
    : IRequest<Account>, IRequirePermission, IOrganizationScoped
{
    public string PermissionKey => PermissionKeys.AccountView;
}
