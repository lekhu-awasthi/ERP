using ErpApp.Domain.Common;

namespace ErpApp.Domain.Configuration;

/// <summary>
/// Tenant-scoped named list of payment due-day terms (architecture-spec.md §4.10), e.g. "Net 30".
/// Referenced by Sales/Purchase documents from Phase 4+ (not yet -- nothing consumes this today).
/// </summary>
public sealed class CreditTerm : ITenantLookupEntity
{
    public Guid Id { get; private set; }
    public Guid OrganizationId { get; private set; }
    public string Name { get; private set; } = null!;
    public int DueDays { get; private set; }
    public bool IsActive { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }

    private CreditTerm()
    {
    }

    public static CreditTerm Create(Guid organizationId, string name, int dueDays)
    {
        return new CreditTerm
        {
            Id = Guid.NewGuid(),
            OrganizationId = organizationId,
            Name = name,
            DueDays = dueDays,
            IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow,
        };
    }

    public void Update(string name, int dueDays, bool isActive)
    {
        Name = name;
        DueDays = dueDays;
        IsActive = isActive;
    }
}
