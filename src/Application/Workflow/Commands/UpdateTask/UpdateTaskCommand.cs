using ErpApp.Application.Common.Security;
using ErpApp.Domain.Workflow;
using MediatR;

namespace ErpApp.Application.Workflow.Commands.UpdateTask;

public sealed record UpdateTaskCommand(
    Guid OrganizationId,
    Guid Id,
    string Title,
    string? Description,
    Guid? AssignedToUserId,
    DateOnly? DueDate,
    Guid TaskTypeId,
    TaskPriority Priority,
    bool IsPrivate)
    : IRequest<UpdateTaskResult>, IRequirePermission, IOrganizationScoped
{
    public string PermissionKey => PermissionKeys.TaskManage;
}

public sealed record UpdateTaskResult(Guid Id, string Title, WorkTaskStatus Status);
