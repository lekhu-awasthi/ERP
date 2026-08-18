using ErpApp.Application.Common.Exceptions;
using ErpApp.Application.Common.Persistence;
using ErpApp.Domain.Configuration;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.Configuration.Commands.CreateTaskType;

public sealed class CreateTaskTypeCommandHandler(IAppDbContext db)
    : IRequestHandler<CreateTaskTypeCommand, CreateTaskTypeResult>
{
    public async Task<CreateTaskTypeResult> Handle(CreateTaskTypeCommand request, CancellationToken cancellationToken)
    {
        var nameExists = await db.TaskTypes.AnyAsync(
            x => x.OrganizationId == request.OrganizationId && x.Name == request.Name, cancellationToken);

        if (nameExists)
        {
            throw new ConflictException($"A task type named '{request.Name}' already exists.");
        }

        var taskType = TaskType.Create(request.OrganizationId, request.Name, request.Color);
        db.TaskTypes.Add(taskType);
        await db.SaveChangesAsync(cancellationToken);

        return new CreateTaskTypeResult(taskType.Id, taskType.Name, taskType.Color);
    }
}
