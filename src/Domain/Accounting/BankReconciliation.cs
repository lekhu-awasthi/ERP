namespace ErpApp.Domain.Accounting;

/// <summary>
/// One act of reconciling a cash-and-bank <see cref="Account"/>: a statement that some set of
/// imported <see cref="BankStatementLine"/>s and some set of this tenant's own <see cref="GlLine"/>s
/// against that account are <b>the same money</b>.
///
/// <para><b>It joins many to many, and the join lives on the two sides.</b> Read live on
/// 2026-09-21: the reference product's matcher collects <c>bs_ids</c> and <c>tx_ids</c> and posts
/// them together, and both a 1:2 and a 2:2 match came back carrying <i>one</i>
/// <c>reconciliation_id</c> on all four rows. So this record carries only <b>who and when</b>, and
/// membership is a nullable foreign key on each side — not a link table.
/// <b>That is the shape and not a shortcut:</b> a nullable key cannot express a row belonging to two
/// reconciliations at once, while a link table can and would then need a unique index to forbid it.
/// This codebase has twice preferred the model that cannot hold the illegal state over the model
/// that holds it and is policed (phase 51's <c>ProductBatch</c>, phase 52's <c>PrimaryQuantity</c>).
/// </para>
///
/// <para><b>The invariant is that the two sides sum to the same figure</b> — see
/// <see cref="Create"/>. This is not a convenience borrowed from the reference product's UI: its
/// endpoint refuses an unequal pair with <c>400 "transactions cannot be reconciled"</c>, probed
/// directly on 2026-09-21 with 678 against 113.</para>
///
/// <para><b>It posts nothing, and that is the point of the feature.</b> A reconciliation is a
/// statement <i>about</i> two records, not a third record. The account's balance comes from this
/// tenant's documents; the statement is the bank's independent account of the same thing; a
/// reconciliation asserts that a subset of each describes one event. If it posted, it would move
/// the very balance it exists to compare, and the difference it reports would be a function of how
/// much reconciling had been done. So:
/// <list type="bullet">
/// <item>no <c>GlJournalEntry</c>, no <c>GlLine</c>, no <c>StockMovement</c>, no
/// <c>PaymentAllocation</c>;</item>
/// <item>no document number — it is not a document;</item>
/// <item>no Draft/Approve/Void lifecycle, so it never reaches the approval queue. Undoing one is a
/// delete (<c>DELETE /bank-reconciliations/:id</c> in the reference product, which releases both
/// sides), because there is nothing posted to reverse;</item>
/// <item><b>not</b> lock-date sensitive, for <see cref="BankStatementLine"/>'s reason: reconciling
/// a period is something you usually do <i>after</i> closing it, and nothing it writes changes a
/// figure the lock date protects.</item>
/// </list>
/// </para>
///
/// <para><b>What a Void of a reconciled document does: nothing, deliberately.</b> Voiding does not
/// mutate the original entry's lines — it posts a second, reversing entry (phase 16a) — so a
/// reconciled <see cref="GlLine"/> survives its document's Void intact and no key dangles. The
/// reversal appears on the matcher as a new, unreconciled movement, which is the honest outcome:
/// the bank's line still says the money moved, and the reconciliation report's Difference moves by
/// the reversal's value, which is exactly the signal the user needs. Refusing the Void instead
/// would be an interaction nobody has posed (phase 45) and would reach thirteen handlers.
/// <b>Re-entry condition:</b> a user reporting that a voided document left a reconciliation they
/// cannot interpret.</para>
/// </summary>
public sealed class BankReconciliation
{
    public Guid Id { get; private set; }

    public Guid OrganizationId { get; private set; }

    /// <summary>The cash-and-bank <see cref="Account"/> both sides belong to. A reconciliation
    /// never spans two accounts — each side's rows are filtered to this one before they reach
    /// <see cref="Create"/>.</summary>
    public Guid BankAccountId { get; private set; }

    /// <summary>When the reconciling happened — the reference product's <c>reconciled_at</c>, shown
    /// as "Reconciled on" in its detail drawer. An instant, rendered through
    /// <c>NepaliDatePipe</c> like every other timestamp (phase 48).</summary>
    public DateTimeOffset ReconciledAt { get; private set; }

    /// <summary>Who did it — the reference product's <c>reconciled_by</c>, shown as "Reconciled by
    /// &lt;name&gt;". Stored as the user id; the name is resolved for display.</summary>
    public Guid ReconciledByUserId { get; private set; }

    private BankReconciliation()
    {
    }

    /// <summary>
    /// Builds the record, enforcing the one rule the whole feature rests on.
    ///
    /// <para>The two totals arrive as <see cref="BankMovementTotal"/> rather than as decimals
    /// precisely so that this comparison cannot accidentally be made between a statement's sign
    /// convention and a document's — see that type. The counts arrive separately because "the sums
    /// are equal" and "something was actually selected" are two different rules: a zero total with
    /// rows on both sides is legal (two lines netting off), while an empty side is not, and one
    /// decimal cannot tell those apart.</para>
    /// </summary>
    public static BankReconciliation Create(
        Guid organizationId,
        Guid bankAccountId,
        BankMovementTotal statementTotal,
        int statementLineCount,
        BankMovementTotal bookTotal,
        int bookLineCount,
        Guid reconciledByUserId,
        DateTimeOffset now)
    {
        if (organizationId == Guid.Empty)
        {
            throw new ArgumentException("A bank reconciliation must belong to an organization.", nameof(organizationId));
        }

        if (bankAccountId == Guid.Empty)
        {
            throw new ArgumentException("A bank reconciliation must name a bank account.", nameof(bankAccountId));
        }

        if (reconciledByUserId == Guid.Empty)
        {
            throw new ArgumentException("A bank reconciliation must record who made it.", nameof(reconciledByUserId));
        }

        if (statementLineCount <= 0)
        {
            throw new InvalidOperationException(
                "A bank reconciliation needs at least one bank statement line.");
        }

        if (bookLineCount <= 0)
        {
            throw new InvalidOperationException(
                "A bank reconciliation needs at least one book transaction.");
        }

        if (statementTotal != bookTotal)
        {
            throw new InvalidOperationException(
                "The selected bank statement lines and book transactions must total the same amount "
                + $"({statementTotal} against {bookTotal}).");
        }

        return new BankReconciliation
        {
            Id = Guid.NewGuid(),
            OrganizationId = organizationId,
            BankAccountId = bankAccountId,
            ReconciledAt = now,
            ReconciledByUserId = reconciledByUserId,
        };
    }
}
