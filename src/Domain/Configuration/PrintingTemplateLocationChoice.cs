using ErpApp.Domain.Common;

namespace ErpApp.Domain.Configuration;

/// <summary>
/// Phase 75 -- the printing template one location uses for one document type, in place of the organization's default
/// (<see cref="PrintingTemplate.IsDefault"/>). At most one per (organization, type, location).
///
/// <para>Read live on the vendor (phase-75-status.md § Live read): its selection is per type and location, and a
/// location with no selection of its own prints with the "Default" location's. Here the organization's default plays
/// that part, so a location with no row prints exactly as it did before this phase.</para>
///
/// <para>A choice names an active template of its own type; the handlers that could break that (a template moved to
/// another type, or deactivated) remove the choice, and the print handler re-checks both rather than trusting the
/// row, so a stale one falls back to the default instead of printing another type's layout.</para>
/// </summary>
public sealed class PrintingTemplateLocationChoice
{
    public Guid Id { get; private set; }
    public Guid OrganizationId { get; private set; }
    public DocumentType DocumentType { get; private set; }
    public Guid LocationId { get; private set; }
    public Guid PrintingTemplateId { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }

    private PrintingTemplateLocationChoice()
    {
    }

    /// <summary>The template must be the same type as the choice; whether it exists, is the organization's and is
    /// active are database reads, so the handler checks them.</summary>
    public static PrintingTemplateLocationChoice Create(Guid organizationId, Guid locationId, PrintingTemplate template)
    {
        if (template.OrganizationId != organizationId)
        {
            throw new InvalidOperationException("The template belongs to another organization.");
        }

        return new PrintingTemplateLocationChoice
        {
            Id = Guid.NewGuid(),
            OrganizationId = organizationId,
            DocumentType = template.DocumentType,
            LocationId = locationId,
            PrintingTemplateId = template.Id,
            CreatedAt = DateTimeOffset.UtcNow,
        };
    }

    /// <summary>Points this location at another template of the same type.</summary>
    public void Choose(PrintingTemplate template)
    {
        if (template.OrganizationId != OrganizationId || template.DocumentType != DocumentType)
        {
            throw new InvalidOperationException(
                $"A {DocumentType} location choice can only name a {DocumentType} template of this organization.");
        }

        PrintingTemplateId = template.Id;
    }
}
