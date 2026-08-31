using ErpApp.Application.Common.Exceptions;
using ErpApp.Application.Common.Persistence;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.Configuration.Commands.UpdatePrintingTemplate;

public sealed class UpdatePrintingTemplateCommandHandler(IAppDbContext db)
    : IRequestHandler<UpdatePrintingTemplateCommand, UpdatePrintingTemplateResult>
{
    public async Task<UpdatePrintingTemplateResult> Handle(
        UpdatePrintingTemplateCommand request, CancellationToken cancellationToken)
    {
        var template = await db.PrintingTemplates.SingleOrDefaultAsync(
            x => x.Id == request.Id && x.OrganizationId == request.OrganizationId, cancellationToken)
            ?? throw new NotFoundException("Printing template not found.");

        var nameTaken = await db.PrintingTemplates.AnyAsync(
            x => x.OrganizationId == request.OrganizationId
                 && x.Id != request.Id
                 && x.DocumentType == request.DocumentType
                 && x.Name == request.Name,
            cancellationToken);

        if (nameTaken)
        {
            throw new ConflictException(
                $"A printing template named '{request.Name}' already exists for {request.DocumentType}.");
        }

        template.Update(request.Name, request.DocumentType, request.IsActive);
        await db.SaveChangesAsync(cancellationToken);

        return new UpdatePrintingTemplateResult(
            template.Id, template.Name, template.DocumentType, template.IsDefault, template.IsActive);
    }
}
