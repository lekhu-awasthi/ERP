using ErpApp.Domain.Common;

namespace ErpApp.Domain.Tenancy;

/// <summary>
/// Minimal single-column lookup (Name only) added in Phase 5 specifically to unblock Invoice's
/// required WarehouseId (architecture-spec.md §3.5's "Warehouse is required specifically on
/// Invoice and PurchaseBill" finding) -- a full Warehouse management screen (address, default
/// location flags, etc.) is out of scope this phase per the roadmap brief; this is "build the
/// seam, not the feature", same judgment call as JournalVoucherStatus.Void. Implements
/// ITenantLookupEntity to reuse the generic ListLookupsQuery&lt;TLookup&gt;/
/// DeleteLookupCommand&lt;TLookup&gt; handlers, same as UnitOfMeasurement.
/// </summary>
public sealed class Warehouse : ITenantLookupEntity
{
    public Guid Id { get; private set; }
    public Guid OrganizationId { get; private set; }
    public string Name { get; private set; } = null!;
    public bool IsActive { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }

    private Warehouse()
    {
    }

    public static Warehouse Create(Guid organizationId, string name)
    {
        return new Warehouse
        {
            Id = Guid.NewGuid(),
            OrganizationId = organizationId,
            Name = name,
            IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow,
        };
    }

    public void Update(string name, bool isActive)
    {
        Name = name;
        IsActive = isActive;
    }
}
