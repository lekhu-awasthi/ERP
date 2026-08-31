using ErpApp.Application.Common.Exceptions;
using ErpApp.Application.Common.Persistence;
using ErpApp.Domain.Configuration;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.Configuration.Commands.CreateCustomTemplate;

public sealed class CreateCustomTemplateCommandHandler(IAppDbContext db)
    : IRequestHandler<CreateCustomTemplateCommand, CreateCustomTemplateResult>
{
    public async Task<CreateCustomTemplateResult> Handle(CreateCustomTemplateCommand request, CancellationToken cancellationToken)
    {
        var nameExists = await db.CustomTemplates.AnyAsync(
            x => x.OrganizationId == request.OrganizationId && x.Type == request.Type && x.Name == request.Name,
            cancellationToken);

        if (nameExists)
        {
            throw new ConflictException($"A custom template named '{request.Name}' already exists for {request.Type}.");
        }

        var isFirstForType = !await db.CustomTemplates.AnyAsync(
            x => x.OrganizationId == request.OrganizationId && x.Type == request.Type, cancellationToken);

        var template = CustomTemplate.Create(request.OrganizationId, request.Name, request.Type, request.Body, isFirstForType);
        db.CustomTemplates.Add(template);
        await db.SaveChangesAsync(cancellationToken);

        return new CreateCustomTemplateResult(template.Id, template.Name, template.Type, template.Body, template.IsDefault);
    }
}
