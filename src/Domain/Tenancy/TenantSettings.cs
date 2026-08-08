namespace ErpApp.Domain.Tenancy;

/// <summary>
/// Single-row-per-tenant settings aggregate. Seeded with sensible defaults at Organization
/// creation (roadmap Phase 1b task 3) so it always exists by the time Phase 2 adds the real
/// configurable fields (Suggest Selling Price mode, Product Price Basis, Inventory Tracking
/// mode, Negative Cash/Stock Balance actions -- architecture-spec.md §4.10) -- this phase only
/// needs the row to exist, not the fields it will eventually carry.
/// </summary>
public sealed class TenantSettings
{
    public Guid Id { get; private set; }
    public Guid OrganizationId { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }

    private TenantSettings()
    {
    }

    public static TenantSettings CreateDefault(Guid organizationId)
    {
        return new TenantSettings
        {
            Id = Guid.NewGuid(),
            OrganizationId = organizationId,
            CreatedAt = DateTimeOffset.UtcNow,
        };
    }
}
