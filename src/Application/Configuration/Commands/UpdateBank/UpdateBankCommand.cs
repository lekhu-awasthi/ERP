using ErpApp.Application.Common.Security;
using MediatR;

namespace ErpApp.Application.Configuration.Commands.UpdateBank;

public sealed record UpdateBankCommand(Guid OrganizationId, Guid Id, string Name, bool IsActive)
    : IRequest<UpdateBankResult>, IRequirePermission, IOrganizationScoped
{
    public string PermissionKey => PermissionKeys.BankManage;
}

public sealed record UpdateBankResult(Guid Id, string Name, bool IsActive);
