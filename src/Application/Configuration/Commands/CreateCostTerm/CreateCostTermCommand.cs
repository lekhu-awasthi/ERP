using ErpApp.Application.Common.Security;
using ErpApp.Domain.Configuration;
using MediatR;

namespace ErpApp.Application.Configuration.Commands.CreateCostTerm;

public sealed record CreateCostTermCommand(Guid OrganizationId, string Name, CostTermCategory Category)
    : IRequest<CreateCostTermResult>, IRequirePermission, IOrganizationScoped
{
    public string PermissionKey => PermissionKeys.CostTermManage;
}

public sealed record CreateCostTermResult(Guid Id, string Name, CostTermCategory Category);
