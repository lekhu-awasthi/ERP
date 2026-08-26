using ErpApp.Application.Common.Exceptions;
using ErpApp.Application.Common.Persistence;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.Configuration.Commands.UpdateCustomFieldDefinition;

public sealed class UpdateCustomFieldDefinitionCommandHandler(IAppDbContext db)
    : IRequestHandler<UpdateCustomFieldDefinitionCommand, UpdateCustomFieldDefinitionResult>
{
    public async Task<UpdateCustomFieldDefinitionResult> Handle(
        UpdateCustomFieldDefinitionCommand request, CancellationToken cancellationToken)
    {
        var definition = await db.CustomFieldDefinitions.SingleOrDefaultAsync(
            x => x.Id == request.Id && x.OrganizationId == request.OrganizationId, cancellationToken)
            ?? throw new NotFoundException("Custom field not found.");

        var nameTaken = await db.CustomFieldDefinitions.AnyAsync(
            x => x.OrganizationId == request.OrganizationId && x.Id != request.Id && x.Name == request.Name,
            cancellationToken);

        if (nameTaken)
        {
            throw new ConflictException($"A custom field named '{request.Name}' already exists.");
        }

        definition.Update(request.Name, request.Type, request.ApplicableDocumentTypes, request.IsActive, request.ChoiceOptions);
        await db.SaveChangesAsync(cancellationToken);

        return new UpdateCustomFieldDefinitionResult(
            definition.Id,
            definition.Name,
            definition.Type,
            definition.ApplicableDocumentTypes,
            definition.IsActive,
            definition.ChoiceOptions);
    }
}
