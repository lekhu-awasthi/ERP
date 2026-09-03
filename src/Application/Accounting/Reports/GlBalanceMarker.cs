namespace ErpApp.Application.Accounting.Reports;

/// <summary>
/// Phase 26a -- the "DR"/"CR" suffix the reference product prints beside every ledger balance
/// ("16638.45 CR"). The reports carry the balance as a non-negative magnitude plus this marker,
/// never a signed number, so no template has to know which side is normal for which account --
/// the same split <c>ContactStatementQuery</c>'s Balance/BalanceType pair already uses.
///
/// <para>The marker follows the <b>raw net position</b> (net debit -&gt; DR), not the account's
/// natural side. That is deliberate and matches the live report, where an Income account that has
/// been debited on balance prints "DR": the point of the column is to say which way the balance
/// actually leans, and normalising it to the account's expected side would hide exactly the
/// anomaly a reader is scanning for.</para>
/// </summary>
public static class GlBalanceMarker
{
    public const string Debit = "DR";
    public const string Credit = "CR";

    /// <summary>Zero reports DR, the side a zero balance conventionally sits on -- the caller
    /// renders the magnitude, which is 0 either way.</summary>
    public static string For(decimal netDebit) => netDebit >= 0 ? Debit : Credit;

    /// <summary>The non-negative magnitude that pairs with <see cref="For"/>.</summary>
    public static decimal Magnitude(decimal netDebit) => Math.Abs(netDebit);
}
