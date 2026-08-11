using ErpApp.Domain.Common;

namespace ErpApp.Domain.Configuration;

/// <summary>
/// Tenant-scoped named list of payment modes (architecture-spec.md §4.10), e.g. "Cash", "Bank
/// Transfer", "Cheque". Referenced by Payment documents from Phase 4+ (not yet).
/// </summary>
public sealed class PaymentMode : ITenantLookupEntity
{
    public Guid Id { get; private set; }
    public Guid OrganizationId { get; private set; }
    public string Name { get; private set; } = null!;
    public bool IsActive { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }

    private PaymentMode()
    {
    }

    public static PaymentMode Create(Guid organizationId, string name)
    {
        return new PaymentMode
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
