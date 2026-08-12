using ErpApp.Application.Common.Security;
using MediatR;

namespace ErpApp.Application.Configuration.Commands.UpdateTdsType;

public sealed record UpdateTdsTypeCommand(Guid OrganizationId, Guid Id, string Code, string Name, decimal RatePct, bool IsActive)
    : IRequest<UpdateTdsTypeResult>, IRequirePermission, IOrganizationScoped
{
    public string PermissionKey => PermissionKeys.TdsTypeManage;
}

public sealed record UpdateTdsTypeResult(Guid Id, string Code, string Name, decimal RatePct, bool IsActive);
