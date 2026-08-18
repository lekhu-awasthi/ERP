using ErpApp.Application.Common.Security;
using MediatR;

namespace ErpApp.Application.Configuration.Commands.UpdateTaskType;

public sealed record UpdateTaskTypeCommand(Guid OrganizationId, Guid Id, string Name, string Color, bool IsActive)
    : IRequest<UpdateTaskTypeResult>, IRequirePermission, IOrganizationScoped
{
    public string PermissionKey => PermissionKeys.TaskTypeManage;
}

public sealed record UpdateTaskTypeResult(Guid Id, string Name, string Color, bool IsActive);
