using ErpApp.Application.Common.Security;
using ErpApp.Domain.Workflow;
using MediatR;

namespace ErpApp.Application.Workflow.Commands.UpdateTaskStatus;

/// <summary>Covers both Pending->Started and Started/Pending->Done (WorkTask.TransitionStatus only
/// allows a strictly-forward move) -- one command, not separate StartTask/CompleteTask commands, per
/// architecture-spec.md's precedent of keying a status change off the target state rather than the
/// verb (e.g. UpdateTaskStatusCommand mirrors how every ApproveXCommand is one command per document
/// type, not per possible prior state).</summary>
public sealed record UpdateTaskStatusCommand(Guid OrganizationId, Guid Id, WorkTaskStatus NewStatus)
    : IRequest<UpdateTaskStatusResult>, IRequirePermission, IOrganizationScoped
{
    public string PermissionKey => PermissionKeys.TaskManage;
}

public sealed record UpdateTaskStatusResult(Guid Id, WorkTaskStatus Status);
