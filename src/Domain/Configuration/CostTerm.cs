using ErpApp.Domain.Common;

namespace ErpApp.Domain.Configuration;

/// <summary>
/// Tenant-scoped named cost term (architecture-spec.md §4.10, erp-module-scan.md Configurations
/// §7), split by <see cref="CostTermCategory"/> into landed-cost terms and production-cost terms.
/// Prerequisite reference data only -- nothing consumes this today; Phase 25's Bill of Materials
/// and Production Journal are its intended readers (roadmap Phase 25 item 1), the same
/// "lookup lands a phase or more before its consumer" precedent CreditTerm set in Phase 2.
/// </summary>
public sealed class CostTerm : ITenantLookupEntity
{
    public Guid Id { get; private set; }
    public Guid OrganizationId { get; private set; }
    public string Name { get; private set; } = null!;
    public CostTermCategory Category { get; private set; }
    public bool IsActive { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }

    private CostTerm()
    {
    }

    public static CostTerm Create(Guid organizationId, string name, CostTermCategory category)
    {
        return new CostTerm
        {
            Id = Guid.NewGuid(),
            OrganizationId = organizationId,
            Name = name,
            Category = category,
            IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow,
        };
    }

    public void Update(string name, CostTermCategory category, bool isActive)
    {
        Name = name;
        Category = category;
        IsActive = isActive;
    }
}
