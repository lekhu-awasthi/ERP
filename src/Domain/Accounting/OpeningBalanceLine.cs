namespace ErpApp.Domain.Accounting;

/// <summary>
/// Phase 17 (Configurations §18, docs/phase-17-status.md) -- a "day-zero" per-account opening
/// balance, one row per (OrganizationId, AccountId). No Location field -- live-confirmed against
/// the Tigg reference product's own Opening Balances screen, which showed none either (that
/// tenant's Location entitlement isn't on, and this codebase has no Location concept at all).
/// Debit/Credit mirrors JournalVoucherLine's own shape (exactly one non-zero) rather than an
/// Amount+DR/CR-toggle pair -- same "one of two columns nonzero" convention already established
/// for manual GL entry.
///
/// Unlike every ApprovableTransaction, there is no Draft/Approve lifecycle -- the confirmed live
/// screen is a single inline "Save Changes" form with no separate approval step (matches FR-3.4's
/// View/Edit-only permission shape, no Approve key). Saving posts a balanced GlJournalEntry
/// immediately (CreateOrUpdateOpeningBalanceLineCommandHandler); editing an existing line reverses
/// its own prior posting first (GlJournalEntry.PostReversalOf, the same Phase 16a mechanism, not a
/// hand-derived reversal) before posting the corrected one.
/// </summary>
public sealed class OpeningBalanceLine
{
    public Guid Id { get; private set; }
    public Guid OrganizationId { get; private set; }
    public Guid AccountId { get; private set; }
    public decimal Debit { get; private set; }
    public decimal Credit { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    private OpeningBalanceLine()
    {
    }

    public static OpeningBalanceLine Create(Guid organizationId, Guid accountId, decimal debit, decimal credit)
    {
        ValidateSides(debit, credit);

        var now = DateTimeOffset.UtcNow;
        return new OpeningBalanceLine
        {
            Id = Guid.NewGuid(),
            OrganizationId = organizationId,
            AccountId = accountId,
            Debit = debit,
            Credit = credit,
            CreatedAt = now,
            UpdatedAt = now,
        };
    }

    public void Update(decimal debit, decimal credit)
    {
        ValidateSides(debit, credit);
        Debit = debit;
        Credit = credit;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    private static void ValidateSides(decimal debit, decimal credit)
    {
        if (debit < 0 || credit < 0)
        {
            throw new InvalidOperationException("An opening balance's Debit/Credit cannot be negative.");
        }

        if ((debit > 0) == (credit > 0))
        {
            throw new InvalidOperationException("An opening balance must have exactly one of Debit/Credit greater than zero.");
        }
    }
}
