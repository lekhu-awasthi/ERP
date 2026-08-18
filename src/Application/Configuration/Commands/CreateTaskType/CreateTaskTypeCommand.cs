using ErpApp.Application.Common.Security;
using MediatR;

namespace ErpApp.Application.Configuration.Commands.CreateTaskType;

public sealed record CreateTaskTypeCommand(Guid OrganizationId, string Name, string Color)
    : IRequest<CreateTaskTypeResult>, IRequirePermission, IOrganizationScoped
{
    public string PermissionKey => PermissionKeys.TaskTypeManage;
}

public sealed record CreateTaskTypeResult(Guid Id, string Name, string Color);
