using ErpApp.Application.Common.Persistence;
using ErpApp.Domain.Common;
using ErpApp.Domain.Configuration;
using FluentValidation;
using FluentValidation.Results;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.Printing;

/// <summary>
/// Phase 73 -- the checks a template's settings need that depend on its document type (phase 79 adds the layout, which
/// must be one the type is offered): each hidden element must
/// be one the type prints and may hide, and each custom field must be this organization's, active, and applicable to
/// the type. Shared by the save and the preview, and thrown as FluentValidation failures so both answer a 400 naming
/// the field (phase 39: a Domain refusal reached through an endpoint is a 500 that tells a caller nothing).
/// </summary>
public static class PrintingTemplateChecks
{
    public static async Task EnsureValidAsync(
        IAppDbContext db,
        Guid organizationId,
        DocumentType documentType,
        PrintLayout layout,
        IReadOnlyList<PrintElement> hiddenElements,
        IReadOnlyList<Guid> printedCustomFieldIds,
        CancellationToken ct)
    {
        var failures = new List<ValidationFailure>();

        // Phase 79 -- the till roll is offered to three types only.
        if (!PrintLayouts.IsOffered(layout, documentType))
        {
            failures.Add(new ValidationFailure("Layout", $"The {layout} layout is not offered for {documentType}."));
        }

        foreach (var element in hiddenElements.Distinct())
        {
            if (!PrintElements.For(documentType).Contains(element))
            {
                failures.Add(new ValidationFailure(
                    "HiddenElements", $"{documentType} does not print {element}, so a template cannot hide it."));
            }
            else if (PrintElements.Locked(documentType).Contains(element))
            {
                failures.Add(new ValidationFailure(
                    "HiddenElements", $"{element} is required on {documentType}, a tax document (VAT Rules Schedule 5), and cannot be hidden."));
            }
        }

        var ids = printedCustomFieldIds.Distinct().ToList();
        if (ids.Count > 0)
        {
            // The applicable types are a converted column, so the type check runs after the load; the rows are this
            // organization's few custom fields, not a list worth narrowing in SQL.
            var fields = await db.CustomFieldDefinitions
                .Where(x => x.OrganizationId == organizationId && ids.Contains(x.Id))
                .ToListAsync(ct);

            foreach (var id in ids)
            {
                var field = fields.SingleOrDefault(x => x.Id == id);
                if (field is null || !field.IsActive)
                {
                    failures.Add(new ValidationFailure("PrintedCustomFieldIds", $"No active custom field {id} exists here."));
                }
                else if (!field.ApplicableDocumentTypes.Contains(documentType))
                {
                    failures.Add(new ValidationFailure(
                        "PrintedCustomFieldIds", $"The custom field '{field.Name}' does not apply to {documentType}."));
                }
            }
        }

        if (failures.Count > 0)
        {
            throw new ValidationException(failures);
        }
    }
}
