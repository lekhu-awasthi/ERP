using ErpApp.Application.Common.Exceptions;
using ErpApp.Application.Common.Persistence;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.Configuration.Commands.SetDefaultPrintingTemplate;

public sealed class SetDefaultPrintingTemplateCommandHandler(IAppDbContext db) : IRequestHandler<SetDefaultPrintingTemplateCommand, Unit>
{
    public async Task<Unit> Handle(SetDefaultPrintingTemplateCommand request, CancellationToken cancellationToken)
    {
        var template = await db.PrintingTemplates.SingleOrDefaultAsync(
            x => x.Id == request.Id && x.OrganizationId == request.OrganizationId, cancellationToken)
            ?? throw new NotFoundException("Printing template not found.");

        var currentDefault = await db.PrintingTemplates.SingleOrDefaultAsync(
            x => x.OrganizationId == request.OrganizationId
                 && x.DocumentType == template.DocumentType
                 && x.IsDefault
                 && x.Id != template.Id,
            cancellationToken);

        currentDefault?.ClearDefault();
        template.MarkAsDefault();

        await db.SaveChangesAsync(cancellationToken);
        return Unit.Value;
    }
}
