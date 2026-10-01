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
///
/// <para><b>Phase 60</b> extends it for the point of sale rather than adding a second "tender" list
/// beside it (phase 59: the vendor's POS reads the ERP's own <c>/payment-modes</c>). It gains a
/// <see cref="Kind"/> (the vendor's Cash | Card | E-Payment | Other) and the cash or bank
/// <see cref="AccountId"/> a tender in this mode settles into. Both are additive: Payment keeps
/// picking a mode and naming its own account exactly as before, and neither field is read by it.
/// Which locations offer a mode at the till is <c>PosLocationPaymentMode</c>, not a field here.</para>
/// </summary>
public sealed class PaymentMode : ITenantLookupEntity
{
    public Guid Id { get; private set; }
    public Guid OrganizationId { get; private set; }
    public string Name { get; private set; } = null!;
    public bool IsActive { get; private set; }
    public bool RequiresChequeDetails { get; private set; }

    /// <summary>Phase 60. <see cref="PaymentModeKind.Other"/> for every mode that existed before the
    /// phase: the kind is a classification nobody had made, and guessing <c>Cash</c> from a name is
    /// the fragile match phase 17 refused for cheques.</summary>
    public PaymentModeKind Kind { get; private set; }

    /// <summary>Phase 60 -- the cash or bank account (phase 17's <c>AccountKind.Cash/Bank</c>) a till
    /// tender in this mode posts to. Optional here because Payment never needed it; required of any
    /// mode linked to a POS location (the link handler refuses otherwise, and so does editing a
    /// linked mode to drop it).</summary>
    public Guid? AccountId { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    private PaymentMode()
    {
    }

    public static PaymentMode Create(
        Guid organizationId,
        string name,
        bool requiresChequeDetails = false,
        PaymentModeKind kind = PaymentModeKind.Other,
        Guid? accountId = null)
    {
        EnsureKind(kind);

        return new PaymentMode
        {
            Id = Guid.NewGuid(),
            OrganizationId = organizationId,
            Name = name,
            IsActive = true,
            RequiresChequeDetails = requiresChequeDetails,
            Kind = kind,
            AccountId = accountId,
            CreatedAt = DateTimeOffset.UtcNow,
        };
    }

    public void Update(
        string name,
        bool isActive,
        bool requiresChequeDetails,
        PaymentModeKind kind = PaymentModeKind.Other,
        Guid? accountId = null)
    {
        EnsureKind(kind);

        Name = name;
        IsActive = isActive;
        RequiresChequeDetails = requiresChequeDetails;
        Kind = kind;
        AccountId = accountId;
    }

    private static void EnsureKind(PaymentModeKind kind)
    {
        if (!Enum.IsDefined(kind))
        {
            throw new InvalidOperationException($"'{kind}' is not a payment mode kind.");
        }
    }
}
