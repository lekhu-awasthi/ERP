using ErpApp.Application.Common.Security;
using ErpApp.Domain.Accounting;
using MediatR;

namespace ErpApp.Application.Accounting.Commands.CreateAccount;

public sealed record CreateAccountCommand(Guid OrganizationId, string Name, Guid GroupId)
    : IRequest<CreateAccountResult>, IRequirePermission, IOrganizationScoped
{
    public string PermissionKey => PermissionKeys.AccountManage;
}

public sealed record CreateAccountResult(Guid Id, string Code, string Name, AccountRootType RootType, Guid GroupId);
