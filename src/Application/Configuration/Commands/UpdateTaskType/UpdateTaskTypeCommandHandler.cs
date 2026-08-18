using ErpApp.Application.Common.Exceptions;
using ErpApp.Application.Common.Persistence;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.Configuration.Commands.UpdateTaskType;

public sealed class UpdateTaskTypeCommandHandler(IAppDbContext db)
    : IRequestHandler<UpdateTaskTypeCommand, UpdateTaskTypeResult>
{
    public async Task<UpdateTaskTypeResult> Handle(UpdateTaskTypeCommand request, CancellationToken cancellationToken)
    {
        var taskType = await db.TaskTypes.SingleOrDefaultAsync(
            x => x.Id == request.Id && x.OrganizationId == request.OrganizationId, cancellationToken)
            ?? throw new NotFoundException("Task type not found.");

        var nameTaken = await db.TaskTypes.AnyAsync(
            x => x.OrganizationId == request.OrganizationId && x.Id != request.Id && x.Name == request.Name,
            cancellationToken);

        if (nameTaken)
        {
            throw new ConflictException($"A task type named '{request.Name}' already exists.");
        }

        taskType.Update(request.Name, request.Color, request.IsActive);
        await db.SaveChangesAsync(cancellationToken);

        return new UpdateTaskTypeResult(taskType.Id, taskType.Name, taskType.Color, taskType.IsActive);
    }
}
