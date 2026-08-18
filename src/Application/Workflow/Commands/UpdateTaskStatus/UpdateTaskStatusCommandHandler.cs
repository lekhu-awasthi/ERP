using ErpApp.Application.Common.Exceptions;
using ErpApp.Application.Common.Persistence;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.Workflow.Commands.UpdateTaskStatus;

public sealed class UpdateTaskStatusCommandHandler(IAppDbContext db)
    : IRequestHandler<UpdateTaskStatusCommand, UpdateTaskStatusResult>
{
    public async Task<UpdateTaskStatusResult> Handle(UpdateTaskStatusCommand request, CancellationToken cancellationToken)
    {
        var task = await db.Tasks.SingleOrDefaultAsync(
            x => x.Id == request.Id && x.OrganizationId == request.OrganizationId, cancellationToken)
            ?? throw new NotFoundException("Task not found.");

        if (request.NewStatus <= task.Status)
        {
            throw new ConflictException($"Cannot move a task from {task.Status} to {request.NewStatus}.");
        }

        task.TransitionStatus(request.NewStatus);
        await db.SaveChangesAsync(cancellationToken);

        return new UpdateTaskStatusResult(task.Id, task.Status);
    }
}
