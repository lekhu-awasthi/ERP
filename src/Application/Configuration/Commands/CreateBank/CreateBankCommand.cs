using ErpApp.Application.Common.Security;
using MediatR;

namespace ErpApp.Application.Configuration.Commands.CreateBank;

public sealed record CreateBankCommand(Guid OrganizationId, string Name)
    : IRequest<CreateBankResult>, IRequirePermission, IOrganizationScoped
{
    public string PermissionKey => PermissionKeys.BankManage;
}

public sealed record CreateBankResult(Guid Id, string Name);
