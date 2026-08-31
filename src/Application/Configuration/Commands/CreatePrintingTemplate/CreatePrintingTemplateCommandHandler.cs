using ErpApp.Application.Common.Exceptions;
using ErpApp.Application.Common.Persistence;
using ErpApp.Domain.Configuration;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.Configuration.Commands.CreatePrintingTemplate;

public sealed class CreatePrintingTemplateCommandHandler(IAppDbContext db)
    : IRequestHandler<CreatePrintingTemplateCommand, CreatePrintingTemplateResult>
{
    public async Task<CreatePrintingTemplateResult> Handle(
        CreatePrintingTemplateCommand request, CancellationToken cancellationToken)
    {
        var nameExists = await db.PrintingTemplates.AnyAsync(
            x => x.OrganizationId == request.OrganizationId
                 && x.DocumentType == request.DocumentType
                 && x.Name == request.Name,
            cancellationToken);

        if (nameExists)
        {
            throw new ConflictException(
                $"A printing template named '{request.Name}' already exists for {request.DocumentType}.");
        }

        // First template created for a DocumentType becomes its default automatically -- there's
        // always exactly one default once at least one row exists, mirroring the reference
        // product's gallery always showing one checkmark.
        var isFirstForDocumentType = !await db.PrintingTemplates.AnyAsync(
            x => x.OrganizationId == request.OrganizationId && x.DocumentType == request.DocumentType,
            cancellationToken);

        var template = PrintingTemplate.Create(request.OrganizationId, request.Name, request.DocumentType, isFirstForDocumentType);
        db.PrintingTemplates.Add(template);
        await db.SaveChangesAsync(cancellationToken);

        return new CreatePrintingTemplateResult(template.Id, template.Name, template.DocumentType, template.IsDefault);
    }
}
