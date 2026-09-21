namespace ErpApp.Domain.Accounting;

/// <summary>
/// One bank-statement line's money, carrying its <b>direction</b> in the type.
///
/// <para><b>Why this is a type and not a <c>decimal</c>.</b> A statement amount is signed from the
/// <i>bank account's</i> point of view -- a deposit is money arriving in the account -- while every
/// other amount this codebase moves around is signed from a document's or a posting rule's point of
/// view. Those two conventions agree until they do not, and phase 56 will put them side by side in
/// a two-pane matcher, which is exactly the moment a bare <c>decimal</c> becomes a sign bug nobody
/// can see. So there is no implicit conversion from <see cref="decimal"/> and no public
/// constructor: a caller has to say <see cref="Deposit"/> or <see cref="Withdrawal"/>, and the
/// compiler error is the feature. This is <c>PrimaryQuantity</c>'s argument (phase 52) in a second
/// place, and it is here because phase 54 found the same bug a third time in a third Void.</para>
///
/// <para><b>One signed value, not a deposit column and a withdrawal column.</b> The reference
/// product stores <c>dr_amount</c> and <c>cr_amount</c> side by side and enforces that exactly one
/// is non-zero -- which is two columns able to contradict each other, guarded by a rule. This
/// codebase has ruled against that shape twice: <c>ProductBatch</c> stores no quantity because a
/// dimension with its own quantity column is phase 37's two-of-three-views drift waiting to happen
/// (phase 51), and <c>PrimaryQuantity</c> is derived and never a column because a third column is a
/// second quantity (phase 52). A signed amount carries the identical information with no state in
/// which it can disagree with itself. This diverges from the vendor's schema and matches its
/// behaviour exactly, which is recorded rather than smoothed over -- the same trade phase 52
/// made.</para>
///
/// <para><b>Zero is not a value here.</b> The reference product rejects a row with neither amount
/// (<i>"Row [N]: both deposit and withdrawal cannot be zero"</i>, read live), and a zero-value
/// statement line would be a row that can never match anything in phase 56.</para>
/// </summary>
public readonly record struct StatementAmount
{
    private StatementAmount(decimal signed) => Signed = signed;

    /// <summary>
    /// Positive for money into the account, negative for money out. The single stored value.
    ///
    /// <para>Never <c>-0m</c>: <see cref="Withdrawal"/> refuses a zero magnitude, so the sign bit
    /// cannot survive on a zero. That matters because <c>decimal</c> keeps a signed zero and it
    /// surfaces as <c>-0.00</c> once a spreadsheet cell casts it to <c>double</c>, and no test
    /// catches it because <c>-0m == 0m</c> (phase 26c).</para>
    /// </summary>
    public decimal Signed { get; }

    /// <summary>Money into the account. <paramref name="magnitude"/> is the positive figure the
    /// user typed in the Deposit column.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The magnitude is zero or negative.</exception>
    public static StatementAmount Deposit(decimal magnitude) =>
        magnitude > 0m
            ? new StatementAmount(magnitude)
            : throw new ArgumentOutOfRangeException(
                nameof(magnitude), magnitude, "A deposit must be a positive amount.");

    /// <summary>Money out of the account. <paramref name="magnitude"/> is the positive figure the
    /// user typed in the Withdrawal column -- callers pass what they read, never a negation.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The magnitude is zero or negative.</exception>
    public static StatementAmount Withdrawal(decimal magnitude) =>
        magnitude > 0m
            ? new StatementAmount(-magnitude)
            : throw new ArgumentOutOfRangeException(
                nameof(magnitude), magnitude, "A withdrawal must be a positive amount.");

    /// <summary>
    /// Rebuilds the value from its stored signed form. <b>For the persistence layer only</b> -- it
    /// is the EF value converter's other half, and taking a raw signed decimal is precisely what
    /// the two factories above exist to stop a handler doing.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">The stored value is zero.</exception>
    public static StatementAmount FromSigned(decimal signed) =>
        signed != 0m
            ? new StatementAmount(signed)
            : throw new ArgumentOutOfRangeException(
                nameof(signed), signed, "A bank statement line cannot carry a zero amount.");

    /// <summary>
    /// False only for <c>default(StatementAmount)</c>, which C# lets any caller construct however
    /// private the constructor is. Nothing inside this type can prevent that, so the aggregate
    /// checks it instead -- see <c>BankStatementLine.Create</c>. Found by
    /// <c>SortSweepGuardTests</c>, which built one by reflection and only discovered it on the way
    /// back out of the database, where the value converter threw.
    /// </summary>
    public bool IsSpecified => Signed != 0m;

    /// <summary>True when this is money into the account.</summary>
    public bool IsDeposit => Signed > 0m;

    /// <summary>The Deposit column's figure: the magnitude when this is a deposit, else zero.</summary>
    public decimal DepositAmount => Signed > 0m ? Signed : 0m;

    /// <summary>The Withdrawal column's figure: the magnitude when this is a withdrawal, else
    /// zero. Positive, because it is what the column displays.</summary>
    public decimal WithdrawalAmount => Signed < 0m ? -Signed : 0m;

    public override string ToString() =>
        IsDeposit ? $"+{Signed}" : Signed.ToString();
}
