namespace ErpApp.Domain.Catalog;

/// <summary>One selectable value of a <see cref="VariantAttribute"/> ("Large", "Blue"). Created
/// and retired only through the parent aggregate, which owns the no-duplicate-value invariant.</summary>
public sealed class VariantAttributeOption
{
    public Guid Id { get; private set; }
    public Guid VariantAttributeId { get; private set; }
    public string Value { get; private set; } = null!;
    public int SortOrder { get; private set; }
    public bool IsActive { get; private set; }

    private VariantAttributeOption()
    {
    }

    internal static VariantAttributeOption Create(Guid variantAttributeId, string value, int sortOrder)
    {
        return new VariantAttributeOption
        {
            Id = Guid.NewGuid(),
            VariantAttributeId = variantAttributeId,
            Value = value,
            SortOrder = sortOrder,
            IsActive = true,
        };
    }

    internal void Rename(string value) => Value = value;

    internal void Deactivate() => IsActive = false;

    internal void Reactivate() => IsActive = true;
}
