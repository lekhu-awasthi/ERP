using ErpApp.Application.Common.Security;
using ErpApp.Domain.Workflow;
using MediatR;

namespace ErpApp.Application.Workflow.Commands.CreateTask;

public sealed record CreateTaskCommand(
    Guid OrganizationId,
    TaskParentType ParentType,
    Guid ParentId,
    string Title,
    string? Description,
    Guid? AssignedToUserId,
    DateOnly? DueDate,
    Guid TaskTypeId,
    TaskPriority Priority,
    bool IsPrivate)
    : IRequest<CreateTaskResult>, IRequirePermission, IOrganizationScoped
{
    public string PermissionKey => PermissionKeys.TaskManage;
}

public sealed record CreateTaskResult(
    Guid Id, TaskParentType ParentType, Guid ParentId, string Title, WorkTaskStatus Status, DateTimeOffset CreatedAt);
