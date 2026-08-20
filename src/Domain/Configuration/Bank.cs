using ErpApp.Domain.Common;

namespace ErpApp.Domain.Configuration;

/// <summary>
/// Tenant-scoped named list of banking institutions (architecture-spec.md Configurations §3,
/// confirmed empty in the original scan). Phase 17: populates the "Select Bank" picker a Bank-kind
/// Account (<see cref="Accounting.AccountKind.Bank"/>) requires -- live-confirmed against the Tigg
/// reference product's "New Bank Account" dialog. Also doubles as the institution list for
/// e-wallets (E-sewa, Khalti) -- those are ordinary Bank-kind accounts pointing at a wallet
/// provider here, not a separate account kind (docs/phase-17-status.md decision #3).
/// </summary>
public sealed class Bank : ITenantLookupEntity
{
    public Guid Id { get; private set; }
    public Guid OrganizationId { get; private set; }
    public string Name { get; private set; } = null!;
    public bool IsActive { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }

    private Bank()
    {
    }

    public static Bank Create(Guid organizationId, string name)
    {
        return new Bank
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
