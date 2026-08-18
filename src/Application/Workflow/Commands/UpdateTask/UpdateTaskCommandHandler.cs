using ErpApp.Application.Common.Exceptions;
using ErpApp.Application.Common.Persistence;
using ErpApp.Domain.Workflow;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.Workflow.Commands.UpdateTask;

public sealed class UpdateTaskCommandHandler(IAppDbContext db) : IRequestHandler<UpdateTaskCommand, UpdateTaskResult>
{
    public async Task<UpdateTaskResult> Handle(UpdateTaskCommand request, CancellationToken cancellationToken)
    {
        var task = await db.Tasks.SingleOrDefaultAsync(
            x => x.Id == request.Id && x.OrganizationId == request.OrganizationId, cancellationToken)
            ?? throw new NotFoundException("Task not found.");

        if (task.Status == WorkTaskStatus.Done)
        {
            throw new ConflictException("A Done task can no longer be edited.");
        }

        await WorkflowValidation.EnsureTaskTypeExistsAsync(db, request.OrganizationId, request.TaskTypeId, cancellationToken);
        await WorkflowValidation.EnsureAssigneeIsAcceptedMemberAsync(
            db, request.OrganizationId, request.AssignedToUserId, cancellationToken);

        task.Update(
            request.Title, request.Description, request.AssignedToUserId, request.DueDate, request.TaskTypeId,
            request.Priority, request.IsPrivate);

        await db.SaveChangesAsync(cancellationToken);

        return new UpdateTaskResult(task.Id, task.Title, task.Status);
    }
}
