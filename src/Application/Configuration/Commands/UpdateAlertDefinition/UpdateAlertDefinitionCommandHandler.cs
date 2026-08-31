using ErpApp.Application.Common.Exceptions;
using ErpApp.Application.Common.Persistence;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.Configuration.Commands.UpdateAlertDefinition;

public sealed class UpdateAlertDefinitionCommandHandler(IAppDbContext db)
    : IRequestHandler<UpdateAlertDefinitionCommand, UpdateAlertDefinitionResult>
{
    public async Task<UpdateAlertDefinitionResult> Handle(
        UpdateAlertDefinitionCommand request, CancellationToken cancellationToken)
    {
        var alert = await db.AlertDefinitions.SingleOrDefaultAsync(
            x => x.Id == request.Id && x.OrganizationId == request.OrganizationId, cancellationToken)
            ?? throw new NotFoundException("Alert not found.");

        var nameTaken = await db.AlertDefinitions.AnyAsync(
            x => x.OrganizationId == request.OrganizationId && x.Id != request.Id && x.Name == request.Name,
            cancellationToken);

        if (nameTaken)
        {
            throw new ConflictException($"An alert named '{request.Name}' already exists.");
        }

        // Editing an alert never rewrites its already-written AlertSendLog rows, and never
        // "un-sends" today's occurrence: changing the schedule time to a later slot today does not
        // resurrect an occurrence that already fired, because the ledger key is (definition, date)
        // and takes no notice of the time. Retiming an alert therefore takes effect tomorrow -- the
        // same behaviour a cron edit has, and the one that cannot double-mail anyone.
        alert.Update(
            request.Name,
            request.Medium,
            request.AlertType,
            request.Recipients,
            request.Frequency,
            request.ScheduleTime,
            request.IsActive);

        await db.SaveChangesAsync(cancellationToken);

        return new UpdateAlertDefinitionResult(
            alert.Id, alert.Name, alert.Medium, alert.AlertType, alert.Recipients,
            alert.Frequency, alert.ScheduleTime, alert.IsActive);
    }
}
