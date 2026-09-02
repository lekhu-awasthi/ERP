using ErpApp.Application.Common.Exceptions;
using ErpApp.Application.Common.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.Catalog.Variants;

/// <summary>Loads the attribute/option name maps every variant handler needs for display, and
/// validates that a submitted (attributeId, optionId) set actually exists in this tenant's catalog
/// and that each option really belongs to the attribute it was submitted under -- without which a
/// caller could pair Color's id with a Size option's id and produce a variant whose combination
/// reads as nonsense.</summary>
public sealed class VariantCatalogLookup
{
    public required IReadOnlyDictionary<Guid, string> AttributeNames { get; init; }

    public required IReadOnlyDictionary<Guid, string> OptionValues { get; init; }

    public required IReadOnlyDictionary<Guid, Guid> OptionToAttribute { get; init; }

    public static async Task<VariantCatalogLookup> LoadAsync(
        IAppDbContext db, Guid organizationId, CancellationToken cancellationToken)
    {
        var attributes = await db.VariantAttributes
            .Where(x => x.OrganizationId == organizationId)
            .Select(x => new { x.Id, x.Name })
            .ToListAsync(cancellationToken);

        var attributeIds = attributes.ConvertAll(x => x.Id);

        var options = await db.VariantAttributeOptions
            .Where(x => attributeIds.Contains(x.VariantAttributeId))
            .Select(x => new { x.Id, x.Value, x.VariantAttributeId })
            .ToListAsync(cancellationToken);

        return new VariantCatalogLookup
        {
            AttributeNames = attributes.ToDictionary(x => x.Id, x => x.Name),
            OptionValues = options.ToDictionary(x => x.Id, x => x.Value),
            OptionToAttribute = options.ToDictionary(x => x.Id, x => x.VariantAttributeId),
        };
    }

    public void EnsureValid(IEnumerable<VariantCombinationInput> pairs)
    {
        foreach (var pair in pairs)
        {
            if (!AttributeNames.ContainsKey(pair.AttributeId))
            {
                throw new NotFoundException("Variant attribute not found.");
            }

            if (!OptionToAttribute.TryGetValue(pair.OptionId, out var owner))
            {
                throw new NotFoundException("Variant attribute option not found.");
            }

            if (owner != pair.AttributeId)
            {
                throw new ConflictException("That option does not belong to the attribute it was submitted under.");
            }
        }
    }
}
