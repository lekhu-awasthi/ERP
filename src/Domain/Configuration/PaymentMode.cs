using ErpApp.Domain.Common;

namespace ErpApp.Domain.Configuration;

/// <summary>
/// Tenant-scoped named list of payment modes (architecture-spec.md §4.10), e.g. "Cash", "Bank
/// Transfer", "Cheque". Referenced by Payment documents from Phase 4+.
///
/// <see cref="RequiresChequeDetails"/> (Phase 17, docs/phase-17-status.md decision #6) is how a
/// mode marks itself as "picking this mode means a Cheque is involved" -- deliberately a tenant-set
/// flag rather than matching on the literal string "Cheque" (fragile: renamed/duplicated/localized
/// mode names would silently break a name match). CreatePaymentCommand reads this flag off the
/// chosen mode to decide whether to also create a linked Payments.Cheque row.
/// </summary>
public sealed class PaymentMode : ITenantLookupEntity
{
    public Guid Id { get; private set; }
    public Guid OrganizationId { get; private set; }
    public string Name { get; private set; } = null!;
    public bool IsActive { get; private set; }
    public bool RequiresChequeDetails { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }

    private PaymentMode()
    {
    }

    public static PaymentMode Create(Guid organizationId, string name, bool requiresChequeDetails = false)
    {
        return new PaymentMode
        {
            Id = Guid.NewGuid(),
            OrganizationId = organizationId,
            Name = name,
            IsActive = true,
            RequiresChequeDetails = requiresChequeDetails,
            CreatedAt = DateTimeOffset.UtcNow,
        };
    }

    public void Update(string name, bool isActive, bool requiresChequeDetails)
    {
        Name = name;
        IsActive = isActive;
        RequiresChequeDetails = requiresChequeDetails;
    }
}
