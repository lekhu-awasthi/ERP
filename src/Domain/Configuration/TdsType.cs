using ErpApp.Domain.Common;

namespace ErpApp.Domain.Configuration;

/// <summary>
/// Tenant-scoped named list of Nepal IRD withholding-tax categories (architecture-spec.md §4.10),
/// e.g. "11111 Individual or Proprietorship Firm". erp-module-scan.md flags this as "likely
/// system-seeded reference data, versioned per fiscal year" but that's speculative -- no fiscal-
/// year versioning is built this phase, just a tenant-editable list (see phase-6-status.md's scope
/// decisions). Referenced by PurchaseBill/Expense's optional TdsTypeId.
/// </summary>
public sealed class TdsType : ITenantLookupEntity
{
    public Guid Id { get; private set; }
    public Guid OrganizationId { get; private set; }
    public string Code { get; private set; } = null!;
    public string Name { get; private set; } = null!;
    public decimal RatePct { get; private set; }
    public bool IsActive { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }

    private TdsType()
    {
    }

    public static TdsType Create(Guid organizationId, string code, string name, decimal ratePct)
    {
        return new TdsType
        {
            Id = Guid.NewGuid(),
            OrganizationId = organizationId,
            Code = code,
            Name = name,
            RatePct = ratePct,
            IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow,
        };
    }

    public void Update(string code, string name, decimal ratePct, bool isActive)
    {
        Code = code;
        Name = name;
        RatePct = ratePct;
        IsActive = isActive;
    }
}
