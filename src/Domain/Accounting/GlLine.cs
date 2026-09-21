namespace ErpApp.Domain.Accounting;

/// <summary>
/// Child line of a posted GlJournalEntry -- own table, created only via GlJournalEntry.Post.
/// </summary>
public sealed class GlLine
{
    public Guid Id { get; private set; }
    public Guid GlJournalEntryId { get; private set; }
    public Guid AccountId { get; private set; }
    public decimal Debit { get; private set; }
    public decimal Credit { get; private set; }

    /// <summary>
    /// Phase 56 — the <see cref="BankReconciliation"/> this line was matched into, or null.
    ///
    /// <para><b>Why the line and not the entry or the document.</b> What a bank statement line
    /// corresponds to is one movement of money into or out of one bank account, and a GL line
    /// against that account is exactly that. A <i>document</i> is too coarse in both directions:
    /// phase 36 established that one document can post more than one entry (a receipt covering a
    /// shortfall posts its cost catch-up against the same source document), and a document may
    /// touch the bank account twice or not at all. An <i>entry</i> is too coarse for the same
    /// reason one level down. The line is the only unit that is one movement.</para>
    ///
    /// <para><b>Why a mutable column on an append-only fact row.</b> What makes
    /// <c>GlLine</c> append-only is its <i>money</i>: <see cref="Debit"/>, <see cref="Credit"/> and
    /// <see cref="AccountId"/> are write-once and stay that way — a correction is a new entry, never
    /// an edit (phase 16a). This column records nothing about the posting; it records that somebody
    /// compared this movement with an external record and found them to be the same event. That is
    /// an annotation whose whole nature is to be set and unset, and storing it anywhere else would
    /// mean a link table that permits a line to sit in two reconciliations at once — see
    /// <see cref="BankReconciliation"/> for why this codebase prefers the model that cannot hold the
    /// illegal state.</para>
    ///
    /// <para><b>A reversal does not inherit it</b>, unlike <c>GlJournalEntry.LocationId</c>. A
    /// location says where a posting happened and a reversal happens in the same place; a
    /// reconciliation says this movement was seen on a bank statement, and a reversing movement was
    /// not — it is new money moving the other way, and it has to be matched on its own merits or
    /// left to show up as the difference it is.</para>
    /// </summary>
    public Guid? ReconciliationId { get; private set; }

    private GlLine()
    {
    }

    internal static GlLine Create(Guid glJournalEntryId, Guid accountId, decimal debit, decimal credit)
    {
        return new GlLine
        {
            Id = Guid.NewGuid(),
            GlJournalEntryId = glJournalEntryId,
            AccountId = accountId,
            Debit = debit,
            Credit = credit,
        };
    }

    /// <summary>
    /// Phase 56 — marks this movement as matched into <paramref name="reconciliationId"/>.
    ///
    /// <para>Public, not internal, because its caller is an Application handler — phase-7's rule
    /// that a Domain mutator stays internal only while its sole caller is in this assembly.</para>
    ///
    /// <para>Refusing a line that already carries one is what makes "a row belongs to at most one
    /// reconciliation" an invariant rather than a convention. A handler that has just filtered on
    /// <c>ReconciliationId == null</c> cannot hit this; a handler that forgot to, can.</para>
    /// </summary>
    public void Reconcile(Guid reconciliationId)
    {
        if (reconciliationId == Guid.Empty)
        {
            throw new ArgumentException("A reconciliation id is required.", nameof(reconciliationId));
        }

        if (ReconciliationId is { } existing)
        {
            throw new InvalidOperationException(
                $"This transaction is already reconciled (reconciliation {existing}).");
        }

        ReconciliationId = reconciliationId;
    }

    /// <summary>
    /// Phase 56 — releases this movement back to the unreconciled side. Idempotent: releasing an
    /// already-free line is not an error, because undoing a reconciliation walks both of its sides
    /// and neither side is the authority on what the other still holds.
    /// </summary>
    public void ReleaseFromReconciliation() => ReconciliationId = null;
}

/// <summary>
/// Pure input shape for IGlPostingRule&lt;TDocument&gt;.BuildLines -- not itself persisted; the
/// GlJournalEntry.Post factory turns a list of these into real, entry-linked GlLine rows.
/// </summary>
public sealed record GlLineInput(Guid AccountId, decimal Debit, decimal Credit);
