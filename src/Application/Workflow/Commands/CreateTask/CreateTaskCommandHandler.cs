using ErpApp.Application.Common.Persistence;
using ErpApp.Application.Common.Security;
using ErpApp.Domain.Workflow;
using MediatR;

namespace ErpApp.Application.Workflow.Commands.CreateTask;

public sealed class CreateTaskCommandHandler(IAppDbContext db, ICurrentUserService currentUser)
    : IRequestHandler<CreateTaskCommand, CreateTaskResult>
{
    public async Task<CreateTaskResult> Handle(CreateTaskCommand request, CancellationToken cancellationToken)
    {
        await WorkflowValidation.EnsureParentExistsAsync(
            db, request.OrganizationId, request.ParentType, request.ParentId, cancellationToken);
        await WorkflowValidation.EnsureTaskTypeExistsAsync(db, request.OrganizationId, request.TaskTypeId, cancellationToken);
        await WorkflowValidation.EnsureAssigneeIsAcceptedMemberAsync(
            db, request.OrganizationId, request.AssignedToUserId, cancellationToken);

        var task = WorkTask.Create(
            request.OrganizationId,
            request.ParentType,
            request.ParentId,
            request.Title,
            request.Description,
            request.AssignedToUserId,
            request.DueDate,
            request.TaskTypeId,
            request.Priority,
            request.IsPrivate,
            currentUser.UserId);

        db.Tasks.Add(task);
        await db.SaveChangesAsync(cancellationToken);

        return new CreateTaskResult(task.Id, task.ParentType, task.ParentId, task.Title, task.Status, task.CreatedAt);
    }
}
