using ErpApp.Application.Common.Security;
using ErpApp.Domain.Accounting;
using MediatR;

namespace ErpApp.Application.Accounting.Commands.CreateAccountGroup;

public sealed record CreateAccountGroupCommand(Guid OrganizationId, string Name, AccountRootType RootType, Guid? ParentGroupId)
    : IRequest<CreateAccountGroupResult>, IRequirePermission, IOrganizationScoped
{
    public string PermissionKey => PermissionKeys.AccountGroupManage;
}

public sealed record CreateAccountGroupResult(Guid Id, string Name, AccountRootType RootType, Guid? ParentGroupId);
