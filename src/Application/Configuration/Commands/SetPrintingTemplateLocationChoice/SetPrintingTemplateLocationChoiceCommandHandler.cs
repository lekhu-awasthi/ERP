using ErpApp.Application.Configuration;
using ErpApp.Application.Common.Exceptions;
using ErpApp.Application.Common.Persistence;
using ErpApp.Domain.Configuration;
using ErpApp.Domain.Tenancy;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.Configuration.Commands.SetPrintingTemplateLocationChoice;

public sealed class SetPrintingTemplateLocationChoiceCommandHandler(IAppDbContext db)
    : IRequestHandler<SetPrintingTemplateLocationChoiceCommand, PrintingTemplateLocationChoiceDto>
{
    public async Task<PrintingTemplateLocationChoiceDto> Handle(
        SetPrintingTemplateLocationChoiceCommand request, CancellationToken cancellationToken)
    {
        var template = await db.PrintingTemplates.SingleOrDefaultAsync(
            x => x.Id == request.PrintingTemplateId && x.OrganizationId == request.OrganizationId, cancellationToken)
            ?? throw new NotFoundException("Printing template not found.");

        var location = await db.BillingLocations.SingleOrDefaultAsync(
            x => x.Id == request.LocationId && x.OrganizationId == request.OrganizationId, cancellationToken)
            ?? throw new NotFoundException("Location not found.");

        if (!template.IsActive)
        {
            throw new ConflictException($"'{template.Name}' is inactive, so no location can print with it.");
        }

        // A choice for a type that carries no location here would be stored and never read: the print finds no
        // location on such a document and uses the default. Refused rather than accepted-and-ignored (phase 43).
        var mode = await db.TenantSettings
            .Where(x => x.OrganizationId == request.OrganizationId)
            .Select(x => (LocationScopeMode?)x.LocationScopeMode)
            .SingleOrDefaultAsync(cancellationToken) ?? LocationScopeMode.SalesTransactionsOnly;

        if (!DocumentLocationScope.AppliesTo(template.DocumentType, mode))
        {
            throw new ConflictException(
                $"A {template.DocumentType} carries no location in this organization (Billing Location's scope), " +
                "so it always prints with the organization's default template.");
        }

        var existing = await db.PrintingTemplateLocationChoices.SingleOrDefaultAsync(
            x => x.OrganizationId == request.OrganizationId
                 && x.DocumentType == template.DocumentType
                 && x.LocationId == location.Id,
            cancellationToken);

        if (existing is null)
        {
            existing = PrintingTemplateLocationChoice.Create(request.OrganizationId, location.Id, template);
            db.PrintingTemplateLocationChoices.Add(existing);
        }
        else
        {
            existing.Choose(template);
        }

        await db.SaveChangesAsync(cancellationToken);

        return new PrintingTemplateLocationChoiceDto(existing.LocationId, existing.DocumentType, existing.PrintingTemplateId);
    }
}
