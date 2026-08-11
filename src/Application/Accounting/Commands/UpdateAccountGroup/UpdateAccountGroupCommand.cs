using ErpApp.Application.Common.Security;
using ErpApp.Domain.Accounting;
using MediatR;

namespace ErpApp.Application.Accounting.Commands.UpdateAccountGroup;

public sealed record UpdateAccountGroupCommand(Guid OrganizationId, Guid Id, string Name, Guid? ParentGroupId, bool IsActive)
    : IRequest<UpdateAccountGroupResult>, IRequirePermission, IOrganizationScoped
{
    public string PermissionKey => PermissionKeys.AccountGroupManage;
}

public sealed record UpdateAccountGroupResult(Guid Id, string Name, AccountRootType RootType, Guid? ParentGroupId, bool IsActive);
