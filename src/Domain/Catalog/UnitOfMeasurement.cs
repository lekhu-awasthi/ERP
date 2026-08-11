using ErpApp.Domain.Common;

namespace ErpApp.Domain.Catalog;

/// <summary>Flat lookup for Product's PrimaryUnit/secondary units (architecture-spec.md §4.3),
/// e.g. "Kilogram"/"kgs". Implements ITenantLookupEntity to reuse the generic
/// ListLookupsQuery&lt;TLookup&gt;/DeleteLookupCommand&lt;TLookup&gt; handlers.</summary>
public sealed class UnitOfMeasurement : ITenantLookupEntity
{
    public Guid Id { get; private set; }
    public Guid OrganizationId { get; private set; }
    public string Name { get; private set; } = null!;
    public string ShortName { get; private set; } = null!;
    public bool IsActive { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }

    private UnitOfMeasurement()
    {
    }

    public static UnitOfMeasurement Create(Guid organizationId, string name, string shortName)
    {
        return new UnitOfMeasurement
        {
            Id = Guid.NewGuid(),
            OrganizationId = organizationId,
            Name = name,
            ShortName = shortName,
            IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow,
        };
    }

    public void Update(string name, string shortName, bool isActive)
    {
        Name = name;
        ShortName = shortName;
        IsActive = isActive;
    }
}
