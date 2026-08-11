using ErpApp.Application.Common.Security;
using ErpApp.Domain.Accounting;
using MediatR;

namespace ErpApp.Application.Accounting.Commands.UpdateAccount;

public sealed record UpdateAccountCommand(Guid OrganizationId, Guid Id, string Name, Guid GroupId, bool IsActive)
    : IRequest<UpdateAccountResult>, IRequirePermission, IOrganizationScoped
{
    public string PermissionKey => PermissionKeys.AccountManage;
}

public sealed record UpdateAccountResult(Guid Id, string Name, AccountRootType RootType, Guid GroupId, bool IsActive);
