namespace ErpApp.Domain.Accounting;

/// <summary>
/// A <b>total</b> of money moving through one cash-and-bank account, signed from that account's
/// point of view: positive is money in, negative is money out.
///
/// <para><b>Why this exists beside <see cref="StatementAmount"/>.</b> Phase 56's matcher puts two
/// independently-kept records of the same account side by side — the bank's statement lines and
/// this tenant's own <see cref="GlLine"/>s — and the whole feature turns on one comparison between
/// them. The two sides do not arrive in the same convention. A statement line is already signed
/// from the account's point of view; a GL line is signed from the <i>posting rule's</i>, as a
/// Debit/Credit pair. <b>This type is the one place those two conventions are reconciled</b>, via
/// the two named factories below, so that nothing else in the codebase ever has to know that
/// "debit an asset" and "money in" are the same thing.</para>
///
/// <para><b>Why not just reuse <see cref="StatementAmount"/>.</b> Because a zero is legal here and
/// illegal there, and the difference is not an accident. A statement <i>line</i> of zero is
/// meaningless — the reference product rejects one, and phase 55 made the type refuse one. A
/// statement <i>total</i> of zero is ordinary: two lines of +100 and -100 net to nothing, and the
/// reference product's own gate explicitly permits reconciling a zero-summing selection so long as
/// rows are actually selected on both sides (read live, 2026-09-21). Collapsing the two into one
/// type would force one of those two rules to be wrong.</para>
///
/// <para><b>Why a type at all rather than two decimals.</b> This is <c>PrimaryQuantity</c>'s
/// argument (phase 52) and <see cref="StatementAmount"/>'s (phase 55) in a third place, and the
/// phase-56 kickoff named the moment it earns its keep: a bare <c>decimal</c> holding "the total of
/// what the user ticked" is one refactor away from being compared against a document's Amount,
/// which is signed by a different rule and would read as agreement. There is no implicit conversion
/// from <see cref="decimal"/> and no public constructor.</para>
/// </summary>
public readonly record struct BankMovementTotal
{
    private BankMovementTotal(decimal signed) => Signed = signed;

    /// <summary>Positive for net money into the account, negative for net money out, zero for a
    /// selection that nets off.</summary>
    public decimal Signed { get; }

    /// <summary>A total of nothing. Also <c>default(BankMovementTotal)</c>, and deliberately so —
    /// unlike <see cref="StatementAmount"/> there is no hole here for a default to fall through,
    /// because the empty sum genuinely is zero.</summary>
    public static BankMovementTotal Zero => new(0m);

    /// <summary>
    /// The bank's side. A <see cref="StatementAmount"/> is already signed from the account's point
    /// of view, so this is a plain sum.
    /// </summary>
    public static BankMovementTotal OfStatementLines(IEnumerable<StatementAmount> amounts)
    {
        ArgumentNullException.ThrowIfNull(amounts);

        return new BankMovementTotal(amounts.Sum(x => x.Signed));
    }

    /// <summary>
    /// This tenant's side, and <b>the conversion this type exists for</b>.
    ///
    /// <para>A cash-and-bank account is an asset, so a <b>debit</b> to it is money arriving and a
    /// <b>credit</b> is money leaving — which is the statement's sign convention exactly. Every
    /// caller passes lines already restricted to the one bank account; a line against any other
    /// account is not a movement of this account's money and summing it here would be meaningless,
    /// which is why the restriction is the caller's job and is asserted at every call site rather
    /// than silently filtered here.</para>
    /// </summary>
    public static BankMovementTotal OfGlLines(IEnumerable<GlLine> lines)
    {
        ArgumentNullException.ThrowIfNull(lines);

        return new BankMovementTotal(lines.Sum(x => x.Debit - x.Credit));
    }

    /// <summary>One GL line's own movement, in this convention. The single-line form of
    /// <see cref="OfGlLines"/>, for a row a DTO has to render.</summary>
    public static BankMovementTotal OfGlLine(GlLine line)
    {
        ArgumentNullException.ThrowIfNull(line);

        return new BankMovementTotal(line.Debit - line.Credit);
    }

    /// <summary>
    /// Rebuilds a total the <b>store</b> has already summed — a <c>SUM(Debit - Credit)</c> pushed
    /// down to SQL Server rather than a period's rows dragged into memory to be added up here.
    ///
    /// <para><b>For a query handler only</b>, and it is the one door a raw <see cref="decimal"/> may
    /// come through — which is exactly the role <c>StatementAmount.FromSigned</c> plays for the
    /// persistence layer, and it is named to match. The caller owes what
    /// <see cref="OfGlLines"/> owes: the sum must be over lines against the one bank account, in
    /// <c>Debit - Credit</c> order. Getting that backwards inverts every figure on the
    /// reconciliation report, which is why there is no general-purpose constructor beside it.</para>
    /// </summary>
    public static BankMovementTotal FromGlSum(decimal signedSum) => new(signedSum);

    /// <summary>The bank's figure less this tenant's — the reconciliation report's
    /// <i>Difference</i> row.</summary>
    public static BankMovementTotal operator -(BankMovementTotal left, BankMovementTotal right) =>
        new(left.Signed - right.Signed);

    public override string ToString() => Signed > 0m ? $"+{Signed}" : Signed.ToString();
}
