using ErpApp.Application.Common.Security;
using ErpApp.Domain.Configuration;
using MediatR;

namespace ErpApp.Application.Configuration.Commands.UpdateCostTerm;

public sealed record UpdateCostTermCommand(
    Guid OrganizationId,
    Guid Id,
    string Name,
    CostTermCategory Category,
    bool IsActive)
    : IRequest<UpdateCostTermResult>, IRequirePermission, IOrganizationScoped
{
    public string PermissionKey => PermissionKeys.CostTermManage;
}

public sealed record UpdateCostTermResult(Guid Id, string Name, CostTermCategory Category, bool IsActive);
