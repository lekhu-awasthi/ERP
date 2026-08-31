using ErpApp.Application.Common.Exceptions;
using ErpApp.Application.Common.Persistence;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.Configuration.Commands.SetAlertDefinitionActive;

public sealed class SetAlertDefinitionActiveCommandHandler(IAppDbContext db)
    : IRequestHandler<SetAlertDefinitionActiveCommand, Unit>
{
    public async Task<Unit> Handle(SetAlertDefinitionActiveCommand request, CancellationToken cancellationToken)
    {
        var alert = await db.AlertDefinitions.SingleOrDefaultAsync(
            x => x.Id == request.Id && x.OrganizationId == request.OrganizationId, cancellationToken)
            ?? throw new NotFoundException("Alert not found.");

        alert.SetActive(request.IsActive);
        await db.SaveChangesAsync(cancellationToken);

        return Unit.Value;
    }
}
