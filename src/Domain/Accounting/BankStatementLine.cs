namespace ErpApp.Domain.Accounting;

/// <summary>
/// One line of a bank statement, imported against one cash-and-bank <see cref="Account"/>
/// (phase 55; the feeder for phase 56's reconciliation matcher). Tenant-scoped by
/// <see cref="OrganizationId"/> like every other aggregate here -- there is no EF global query
/// filter in this codebase, so every handler filters manually.
///
/// <para><b>The invariant, stated as an invariant, because the next person to touch this file will
/// otherwise try to give it a lifecycle.</b> A bank statement line is <i>the bank's record of what
/// the bank did</i>, and nothing else. It:
/// <list type="bullet">
/// <item>posts <b>no</b> <c>GlJournalEntry</c> and no <c>GlLine</c>. Importing a statement does not
/// change a single balance -- the account's balance comes from this tenant's own documents, and the
/// whole point of a reconciliation is to compare the two independent records. A statement line that
/// posted would be double-counting by construction;</item>
/// <item>creates <b>no</b> <c>Payment</c>, <c>Cheque</c> or <c>ContactLedger</c> movement, and no
/// stock movement of any kind;</item>
/// <item>draws <b>no</b> number from <c>DocumentNumberGenerator</c> -- it has no document number,
/// because it is not this tenant's document;</item>
/// <item>has <b>no</b> Draft/Approve/Void lifecycle, so it never reaches the approval queue and
/// there is nothing to approve, void or reverse. There is no status property on purpose -- see the
/// note on Status below;</item>
/// <item>is <b>not</b> lock-date sensitive: a statement line is a record of an external fact, and
/// gating it on this tenant's own closing date would make it impossible to reconcile a period
/// after closing it, which is when reconciliation usually happens.</item>
/// </list>
/// This is <c>MigratedSalesRegisterEntry</c>'s shape (phase 21c), for the same reason and with the
/// same warning attached.</para>
///
/// <para><b>Status is derived, and there is deliberately no column for it.</b> The reference
/// product's statement list filters on <c>reconciled</c> with exactly two options -- Reconciled and
/// Pending -- and renders them off whether <c>reconciliation_id</c> is set (read live, 2026-09-21).
/// That is a nullable foreign key with a filter over it, not a modelled lifecycle, which is phase
/// 51's ruling about the serial report's Status filter arriving at the same answer a second time.
/// <b>Phase 56 added that foreign key</b> (<see cref="ReconciliationId"/>) along with the
/// <see cref="BankReconciliation"/> it points at and the Status filter on the list. Phase 55 shipped
/// neither, because a nullable column pointing at a table that does not exist is the
/// present-and-ignored shape phase 43 ruled against.</para>
///
/// <para><b>The line carries no currency and no conversion rate.</b> <see cref="Account"/> has no
/// currency of its own in this codebase -- phase 28 put multi-currency on documents, not on the
/// chart of accounts -- so a statement line is in the tenant's base currency like the account it
/// belongs to. Nothing here needs <c>ToBase</c>, and nothing here may acquire it without the
/// account acquiring a currency first.</para>
/// </summary>
public sealed class BankStatementLine
{
    public Guid Id { get; private set; }

    public Guid OrganizationId { get; private set; }

    /// <summary>The cash-and-bank <see cref="Account"/> this statement belongs to. A statement line
    /// is meaningless without it, which is why it is the one piece of context an import run carries
    /// outside its rows (<c>ImportJob.BankAccountId</c>).</summary>
    public Guid BankAccountId { get; private set; }

    /// <summary>The bank's own value date for the transaction. Stored AD, like every date in this
    /// tree -- BS is presentation only (phase 23), and the reference product's own template says
    /// <i>"Date Format: Only AD Dates are accepted"</i>.</summary>
    public DateOnly Date { get; private set; }

    /// <summary>
    /// The bank's narration, as printed. Optional: the reference product accepts a row with no
    /// description (read live), and a blank narration is ordinary on a fee or an interest line.
    ///
    /// <para><b>One description, not four.</b> The vendor's row carries
    /// <c>description1</c>..<c>description3</c> beside it, and neither of its two templates has a
    /// column that could fill them; both probe uploads left all three null. A field nothing writes
    /// is not a feature (phase 54).</para>
    /// </summary>
    public string? Description { get; private set; }

    /// <summary>Signed from the account's point of view -- see <see cref="StatementAmount"/> for
    /// why this is a type and why it is one value rather than a deposit column and a withdrawal
    /// column.</summary>
    public StatementAmount Amount { get; private set; }

    /// <summary>
    /// The import run that created this line, which is what makes a whole upload undoable.
    ///
    /// <para>The reference product carries a <c>batch_code</c> on the row for the same purpose, so
    /// a batch is parity rather than invention -- but it already exists here as the
    /// <c>ImportJob</c>, so this is that id rather than a second identifier meaning the same thing.
    /// Nullable because the aggregate does not require an importer to have made it; today nothing
    /// else does.</para>
    /// </summary>
    public Guid? ImportJobId { get; private set; }

    /// <summary>
    /// Phase 56 — the <see cref="BankReconciliation"/> this line was matched into, or null. The
    /// seam phase 55 marked and deliberately left unbuilt, now filled.
    ///
    /// <para>This <b>is</b> the list's Status column: the reference product's filter has exactly two
    /// options, Reconciled and Pending, and renders them off whether <c>reconciliation_id</c> is
    /// set (read live, 2026-09-21). There is still no status property here, and phase 55's reasoning
    /// is why — a nullable foreign key with a filter over it is not a lifecycle, and inventing one
    /// would be phase 51's serial-report mistake in a second place.</para>
    /// </summary>
    public Guid? ReconciliationId { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    private BankStatementLine()
    {
    }

    public static BankStatementLine Create(
        Guid organizationId,
        Guid bankAccountId,
        DateOnly date,
        string? description,
        StatementAmount amount,
        Guid? importJobId,
        DateTimeOffset now)
    {
        if (organizationId == Guid.Empty)
        {
            throw new ArgumentException("A bank statement line must belong to an organization.", nameof(organizationId));
        }

        if (bankAccountId == Guid.Empty)
        {
            throw new ArgumentException("A bank statement line must name a bank account.", nameof(bankAccountId));
        }

        // StatementAmount's factories refuse a zero, but C# hands every caller default(T) whatever
        // the constructor's accessibility, and a default one is a zero. The type cannot close that
        // hole; this can, and it has to, because the EF value converter throws on the way back out
        // and the failure would surface as a list that will not load rather than as a bad write.
        if (!amount.IsSpecified)
        {
            throw new ArgumentException(
                "A bank statement line must carry a deposit or a withdrawal; build one with "
                + "StatementAmount.Deposit or StatementAmount.Withdrawal.",
                nameof(amount));
        }

        return new BankStatementLine
        {
            Id = Guid.NewGuid(),
            OrganizationId = organizationId,
            BankAccountId = bankAccountId,
            Date = date,
            Description = string.IsNullOrWhiteSpace(description) ? null : description.Trim(),
            Amount = amount,
            ImportJobId = importJobId,
            CreatedAt = now,
        };
    }

    /// <summary>
    /// Phase 56 — marks this line as matched into <paramref name="reconciliationId"/>. See
    /// <c>GlLine.Reconcile</c>, which carries the same rule for the same reason: refusing a line
    /// that already holds one is what makes "at most one reconciliation" an invariant.
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
                $"This bank statement line is already reconciled (reconciliation {existing}).");
        }

        ReconciliationId = reconciliationId;
    }

    /// <summary>Phase 56 — releases this line back to the unreconciled side. Idempotent, for
    /// <c>GlLine.ReleaseFromReconciliation</c>'s reason.</summary>
    public void ReleaseFromReconciliation() => ReconciliationId = null;
}
