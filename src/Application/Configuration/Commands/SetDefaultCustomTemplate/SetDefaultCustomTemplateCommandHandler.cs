using ErpApp.Application.Common.Exceptions;
using ErpApp.Application.Common.Persistence;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.Configuration.Commands.SetDefaultCustomTemplate;

public sealed class SetDefaultCustomTemplateCommandHandler(IAppDbContext db) : IRequestHandler<SetDefaultCustomTemplateCommand, Unit>
{
    public async Task<Unit> Handle(SetDefaultCustomTemplateCommand request, CancellationToken cancellationToken)
    {
        var template = await db.CustomTemplates.SingleOrDefaultAsync(
            x => x.Id == request.Id && x.OrganizationId == request.OrganizationId, cancellationToken)
            ?? throw new NotFoundException("Custom template not found.");

        var currentDefault = await db.CustomTemplates.SingleOrDefaultAsync(
            x => x.OrganizationId == request.OrganizationId
                 && x.Type == template.Type
                 && x.IsDefault
                 && x.Id != template.Id,
            cancellationToken);

        currentDefault?.ClearDefault();
        template.MarkAsDefault();

        await db.SaveChangesAsync(cancellationToken);
        return Unit.Value;
    }
}
