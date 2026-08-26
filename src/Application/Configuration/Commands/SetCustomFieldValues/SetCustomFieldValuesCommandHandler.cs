using ErpApp.Application.Common.Exceptions;
using ErpApp.Application.Common.Persistence;
using ErpApp.Domain.Common;
using ErpApp.Domain.Configuration;
using FluentValidation;
using FluentValidation.Results;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.Configuration.Commands.SetCustomFieldValues;

public sealed class SetCustomFieldValuesCommandHandler(IAppDbContext db)
    : IRequestHandler<SetCustomFieldValuesCommand, Unit>
{
    public async Task<Unit> Handle(SetCustomFieldValuesCommand request, CancellationToken cancellationToken)
    {
        await EnsureDocumentExistsAsync(db, request.OrganizationId, request.DocumentType, request.DocumentId, cancellationToken);

        var fieldDefinitionIds = request.Values.Select(v => v.FieldDefinitionId).Distinct().ToList();
        var definitions = await db.CustomFieldDefinitions
            .Where(x => x.OrganizationId == request.OrganizationId && fieldDefinitionIds.Contains(x.Id))
            .ToDictionaryAsync(x => x.Id, cancellationToken);

        if (definitions.Count != fieldDefinitionIds.Count)
        {
            throw new NotFoundException("One or more custom fields were not found.");
        }

        foreach (var value in request.Values)
        {
            var definition = definitions[value.FieldDefinitionId];

            if (!definition.ApplicableDocumentTypes.Contains(request.DocumentType))
            {
                throw new ValidationException(
                    [new ValidationFailure(nameof(value.FieldDefinitionId), $"'{definition.Name}' does not apply to {request.DocumentType}.")]);
            }

            if (definition.Type == CustomFieldType.Choices
                && value.Value.Length > 0
                && !definition.ChoiceOptions.Contains(value.Value))
            {
                throw new ValidationException(
                    [new ValidationFailure(nameof(value.Value), $"'{value.Value}' is not a valid option for '{definition.Name}'.")]);
            }
        }

        var existing = await db.CustomFieldValues
            .Where(x => x.OrganizationId == request.OrganizationId
                && x.DocumentType == request.DocumentType && x.DocumentId == request.DocumentId)
            .ToListAsync(cancellationToken);
        db.CustomFieldValues.RemoveRange(existing);

        foreach (var value in request.Values.Where(v => !string.IsNullOrEmpty(v.Value)))
        {
            var definition = definitions[value.FieldDefinitionId];
            db.CustomFieldValues.Add(CustomFieldValue.Create(
                request.OrganizationId, value.FieldDefinitionId, request.DocumentType, request.DocumentId, value.Value, definition.Type));
        }

        await db.SaveChangesAsync(cancellationToken);
        return Unit.Value;
    }

    private static async Task EnsureDocumentExistsAsync(
        IAppDbContext db, Guid organizationId, DocumentType documentType, Guid documentId, CancellationToken cancellationToken)
    {
        var exists = documentType switch
        {
            DocumentType.Quotation => await db.Quotations.AnyAsync(x => x.Id == documentId && x.OrganizationId == organizationId, cancellationToken),
            DocumentType.Invoice => await db.Invoices.AnyAsync(x => x.Id == documentId && x.OrganizationId == organizationId, cancellationToken),
            _ => throw new ArgumentOutOfRangeException(nameof(documentType), documentType, "Custom field values are not wired up for this document type yet."),
        };

        if (!exists)
        {
            throw new NotFoundException($"{documentType} not found.");
        }
    }
}
