using ErpApp.Application.Common.Persistence;
using ErpApp.Domain.Common;
using ErpApp.Domain.Configuration;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.Printing;

/// <summary>
/// Phase 75 -- which template prints a document: the one its location chose for the type, else the organization's
/// default for the type, else <see cref="PrintingTemplateSettings.Default"/> (what every document printed before
/// phase 73). The vendor falls back the same way, from a location's selection to its "Default" location's.
///
/// <para>A choice counts only while it names an <b>active</b> template of the <b>same type</b>. The handlers that
/// deactivate or retype a template remove its choices, but this does not trust them: a stale row falls back to the
/// default rather than printing another type's layout.</para>
/// </summary>
public static class PrintingTemplateResolver
{
    public static async Task<PrintingTemplate?> ResolveAsync(
        IAppDbContext db, Guid organizationId, DocumentType documentType, Guid? locationId, CancellationToken cancellationToken)
    {
        if (locationId is { } location)
        {
            var chosenId = await db.PrintingTemplateLocationChoices
                .Where(x => x.OrganizationId == organizationId && x.DocumentType == documentType && x.LocationId == location)
                .Select(x => (Guid?)x.PrintingTemplateId)
                .SingleOrDefaultAsync(cancellationToken);

            if (chosenId is { } id)
            {
                var chosen = await db.PrintingTemplates.SingleOrDefaultAsync(
                    x => x.Id == id && x.OrganizationId == organizationId && x.DocumentType == documentType && x.IsActive,
                    cancellationToken);

                if (chosen is not null)
                {
                    return chosen;
                }
            }
        }

        return await db.PrintingTemplates.SingleOrDefaultAsync(
            x => x.OrganizationId == organizationId && x.DocumentType == documentType && x.IsDefault,
            cancellationToken);
    }
}
