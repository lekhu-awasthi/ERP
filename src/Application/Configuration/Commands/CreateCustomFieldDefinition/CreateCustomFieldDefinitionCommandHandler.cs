using ErpApp.Application.Common.Exceptions;
using ErpApp.Application.Common.Persistence;
using ErpApp.Domain.Configuration;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.Configuration.Commands.CreateCustomFieldDefinition;

public sealed class CreateCustomFieldDefinitionCommandHandler(IAppDbContext db)
    : IRequestHandler<CreateCustomFieldDefinitionCommand, CreateCustomFieldDefinitionResult>
{
    public async Task<CreateCustomFieldDefinitionResult> Handle(
        CreateCustomFieldDefinitionCommand request, CancellationToken cancellationToken)
    {
        var nameExists = await db.CustomFieldDefinitions.AnyAsync(
            x => x.OrganizationId == request.OrganizationId && x.Name == request.Name, cancellationToken);

        if (nameExists)
        {
            throw new ConflictException($"A custom field named '{request.Name}' already exists.");
        }

        var definition = CustomFieldDefinition.Create(
            request.OrganizationId, request.Name, request.Type, request.ApplicableDocumentTypes, request.ChoiceOptions);
        db.CustomFieldDefinitions.Add(definition);
        await db.SaveChangesAsync(cancellationToken);

        return new CreateCustomFieldDefinitionResult(
            definition.Id, definition.Name, definition.Type, definition.ApplicableDocumentTypes, definition.ChoiceOptions);
    }
}
