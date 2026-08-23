namespace ErpApp.Domain.Crm;

/// <summary>
/// Append-only credit ledger (product-requirements.md FR-4.8's "credit-usage log"); an
/// Organization's current balance is the sum of ChangeAmount across every row, mirroring how
/// Phase 17's OpeningBalanceLine/GlLine derive a running balance rather than storing one mutable
/// counter -- SendSmsCommandHandler's atomicity requirement (a mid-batch failure must leave the
/// balance completely unchanged) is easiest to guarantee when "the balance" is just "sum of
/// committed rows," never a separately-updated counter that could drift from them.
///
/// docs/phase-18-status.md decision #6: credit purchase/billing is out of scope (confirmed live --
/// Tigg's own "Add SMS Credit" is a static "call us" tooltip, not an in-app purchase flow) --
/// ManualAdjustment is how an Admin seeds/corrects a balance, the same "settable starting number,
/// not a payment flow" shape as Phase 17's Opening Balances. Send entries are always negative
/// (ChangeAmount = -TotalCreditsUsed for one SendSmsCommand batch, one ledger row per batch, not per
/// recipient -- keeping Credit History's row count matched to "one row per send event," the same
/// shape as the live Tigg Overview tab's Recent SMS table).
/// </summary>
public sealed class SmsCreditLedgerEntry
{
    public Guid Id { get; private set; }
    public Guid OrganizationId { get; private set; }
    public SmsCreditLedgerEntryType Type { get; private set; }
    public int ChangeAmount { get; private set; }
    public string? Reason { get; private set; }
    public Guid? RelatedSmsBatchId { get; private set; }
    public Guid CreatedByUserId { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }

    private SmsCreditLedgerEntry()
    {
    }

    public static SmsCreditLedgerEntry CreateManualAdjustment(
        Guid organizationId, int changeAmount, string? reason, Guid createdByUserId)
    {
        return new SmsCreditLedgerEntry
        {
            Id = Guid.NewGuid(),
            OrganizationId = organizationId,
            Type = SmsCreditLedgerEntryType.ManualAdjustment,
            ChangeAmount = changeAmount,
            Reason = reason,
            RelatedSmsBatchId = null,
            CreatedByUserId = createdByUserId,
            CreatedAt = DateTimeOffset.UtcNow,
        };
    }

    public static SmsCreditLedgerEntry CreateSendDebit(
        Guid organizationId, int creditsUsed, Guid batchId, Guid createdByUserId)
    {
        return new SmsCreditLedgerEntry
        {
            Id = Guid.NewGuid(),
            OrganizationId = organizationId,
            Type = SmsCreditLedgerEntryType.Send,
            ChangeAmount = -creditsUsed,
            Reason = null,
            RelatedSmsBatchId = batchId,
            CreatedByUserId = createdByUserId,
            CreatedAt = DateTimeOffset.UtcNow,
        };
    }
}
