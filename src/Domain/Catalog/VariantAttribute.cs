namespace ErpApp.Domain.Catalog;

/// <summary>
/// A reusable, tenant-global attribute definition (Size, Color, RAM, ...) with its own list of
/// options -- erp-module-scan.md Inventory §3's "reusable attribute-definition catalog", confirmed
/// there as tenant-wide (11 attributes shared across the catalog) rather than per-product.
///
/// The options are an encapsulated child collection with a private backing field, the same shape as
/// Product.SecondaryUnits. They are never hard-deleted once a ProductVariant references them:
/// <see cref="VariantAttributeOption.Deactivate"/> retires an option from future variant generation
/// while leaving every historical variant (and therefore every stock layer, document line and
/// report row keyed on it) intact and readable. That is the same reasoning as Product.IsActive --
/// a catalog row that transactions point at is not a row a tenant may erase.
/// </summary>
public sealed class VariantAttribute
{
    private readonly List<VariantAttributeOption> _options = [];

    public Guid Id { get; private set; }
    public Guid OrganizationId { get; private set; }
    public string Name { get; private set; } = null!;
    public bool IsActive { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }

    public IReadOnlyList<VariantAttributeOption> Options => _options;

    private VariantAttribute()
    {
    }

    public static VariantAttribute Create(Guid organizationId, string name)
    {
        return new VariantAttribute
        {
            Id = Guid.NewGuid(),
            OrganizationId = organizationId,
            Name = name.Trim(),
            IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow,
        };
    }

    public void Update(string name, bool isActive)
    {
        Name = name.Trim();
        IsActive = isActive;
    }

    /// <summary>Adds an option, rejecting a duplicate value (case-insensitive) against the ones
    /// already present -- including inactive ones, so retiring "Large" and re-adding it would
    /// otherwise silently create a second option that generation treats as distinct.</summary>
    public VariantAttributeOption AddOption(string value)
    {
        var trimmed = value.Trim();

        if (_options.Any(x => string.Equals(x.Value, trimmed, StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidOperationException($"'{trimmed}' is already an option on this attribute.");
        }

        var option = VariantAttributeOption.Create(Id, trimmed, _options.Count);
        _options.Add(option);
        return option;
    }

    public VariantAttributeOption RenameOption(Guid optionId, string value)
    {
        var trimmed = value.Trim();
        var option = FindOption(optionId);

        if (_options.Any(x => x.Id != optionId && string.Equals(x.Value, trimmed, StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidOperationException($"'{trimmed}' is already an option on this attribute.");
        }

        option.Rename(trimmed);
        return option;
    }

    /// <summary>Retires an option from future generation. Deliberately not a removal: existing
    /// ProductVariants keep pointing at it, so their names, stock and history stay intact and the
    /// FK is never orphaned. See the type's own doc comment.</summary>
    public void DeactivateOption(Guid optionId) => FindOption(optionId).Deactivate();

    public void ReactivateOption(Guid optionId) => FindOption(optionId).Reactivate();

    private VariantAttributeOption FindOption(Guid optionId) =>
        _options.SingleOrDefault(x => x.Id == optionId)
        ?? throw new InvalidOperationException("Option not found on this attribute.");
}
