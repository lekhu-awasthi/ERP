using ErpApp.Application.Common.Security;
using MediatR;

namespace ErpApp.Application.Configuration.Commands.CreateTdsType;

public sealed record CreateTdsTypeCommand(Guid OrganizationId, string Code, string Name, decimal RatePct)
    : IRequest<CreateTdsTypeResult>, IRequirePermission, IOrganizationScoped
{
    public string PermissionKey => PermissionKeys.TdsTypeManage;
}

public sealed record CreateTdsTypeResult(Guid Id, string Code, string Name, decimal RatePct);
