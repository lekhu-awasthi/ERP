using ErpApp.Application.Common.Exceptions;
using ErpApp.Application.Common.Persistence;
using ErpApp.Application.Common.Security;
using ErpApp.Domain.Configuration;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.Configuration.Commands.CreateAlertDefinition;

public sealed class CreateAlertDefinitionCommandHandler(IAppDbContext db, ICurrentUserService currentUser)
    : IRequestHandler<CreateAlertDefinitionCommand, CreateAlertDefinitionResult>
{
    public async Task<CreateAlertDefinitionResult> Handle(
        CreateAlertDefinitionCommand request, CancellationToken cancellationToken)
    {
        var nameExists = await db.AlertDefinitions.AnyAsync(
            x => x.OrganizationId == request.OrganizationId && x.Name == request.Name, cancellationToken);

        if (nameExists)
        {
            throw new ConflictException($"An alert named '{request.Name}' already exists.");
        }

        var alert = AlertDefinition.Create(
            request.OrganizationId,
            request.Name,
            request.Medium,
            request.AlertType,
            request.Recipients,
            request.Frequency,
            request.ScheduleTime,
            currentUser.UserId);

        db.AlertDefinitions.Add(alert);
        await db.SaveChangesAsync(cancellationToken);

        return new CreateAlertDefinitionResult(
            alert.Id, alert.Name, alert.Medium, alert.AlertType, alert.Recipients,
            alert.Frequency, alert.ScheduleTime, alert.IsActive);
    }
}
