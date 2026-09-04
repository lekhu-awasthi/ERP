namespace ErpApp.Domain.Common;

/// <summary>
/// The single conversion point between a document's own transaction currency and the base
/// currency every GL line is denominated in (<see cref="CurrencyCatalog.BaseCode"/>) -- the
/// multi-currency counterpart of <see cref="NepalTime"/>, and deliberately the same shape: one
/// tiny static class the whole codebase funnels through, so the rounding rule cannot drift
/// between the eleven document types that post GL.
///
/// <para><b>How the fold works (phase 28, following phase-16b's discount pattern).</b> A document
/// stores its line amounts in its own currency and carries a header CurrencyCode + ExchangeRate.
/// At Approve, the handler converts each *line-level* amount with <see cref="ToBase"/> and hands
/// the converted values to the posting rule, which is then completely unaware that any currency
/// but the base exists. Nothing about <c>GlLine</c>, and nothing in any Phase 8/19/26 report,
/// changed for multi-currency -- exactly as folding discount into <c>Line.Amount</c> left the
/// posting rules untouched in 16b.</para>
///
/// <para><b>Why the conversion happens per line and not per GL line.</b> Every posting rule in
/// this codebase derives its balancing leg as a *sum of the other legs* (InvoicePostingRule's AR
/// line is the sum of revenue + VAT; PurchaseBillPostingRule's AP line likewise). Converting the
/// already-built <c>GlLineInput</c> list would round the balancing leg independently of the legs
/// it balances, so <c>Round(T x r)</c> could differ by a cent from <c>Sum(Round(a_i x r))</c> and
/// <c>GlJournalEntry.Post</c>'s sum(Debit)==sum(Credit) invariant would fail for some rates on
/// some documents -- intermittently, which is the worst possible failure mode. Converting the
/// inputs *before* the rule runs keeps every entry balanced by construction, because the rule then
/// sums the very same rounded numbers it posts. This is the phase-25 lesson restated: build the
/// entry from the values actually created.</para>
///
/// <para><b>What is deliberately NOT converted.</b> Anything already denominated in the base
/// currency: FIFO layer unit costs and the COGS derived from them (<c>StockLedgerEntry.UnitCost</c>
/// is written in base currency at receipt, so an Invoice's COGS leg is already base and converting
/// it again would double-apply the rate), and every historical <c>GlLine</c>.</para>
/// </summary>
public static class ExchangeRates
{
    /// <summary>Scale of a base-currency amount posted to the general ledger. Two, not
    /// <c>GlLine</c>'s stored four: a rupee figure in a report has two decimal places, and
    /// rounding to the presentation scale at the point of posting is what keeps a Trial Balance
    /// from showing sub-paisa dust. (Contrast <c>ProductionJournal.UnitCostScale</c> = 4, which is
    /// a *unit cost*, not a posted amount.)</summary>
    public const int BaseAmountScale = 2;

    /// <summary>Scale an exchange rate is stored and quoted at. Six decimal places covers every
    /// real NPR pair including the weak ones (KRW, IDR) without inviting a rate whose precision
    /// exceeds what a bank actually quotes.</summary>
    public const int RateScale = 6;

    /// <summary>The rate a base-currency document always carries. A document in the base currency
    /// is not a special case anywhere in the code -- it is simply a document whose rate is one, so
    /// <see cref="ToBase"/> is an identity for it and no branch is needed.</summary>
    public const decimal BaseRate = 1m;

    /// <summary>
    /// Converts an amount in a document's own currency into the base currency, rounded to
    /// <see cref="BaseAmountScale"/>. Away-from-zero (not banker's rounding) to match every other
    /// money computation in this codebase and the reference product's own displayed totals.
    /// </summary>
    public static decimal ToBase(decimal transactionAmount, decimal exchangeRate) =>
        Math.Round(transactionAmount * exchangeRate, BaseAmountScale, MidpointRounding.AwayFromZero);

    /// <summary>
    /// Converts a <b>unit cost</b> into the base currency. Separate from <see cref="ToBase"/>
    /// because a unit cost is not a posted amount: it is stored at four decimal places
    /// (StockLedgerEntry.UnitCost, StockMovement.UnitCost, and
    /// <c>ProductionJournal.UnitCostScale</c>), and rounding a foreign unit price to two on the way
    /// in would quietly lose precision on cheap goods -- a 0.0125 USD component at 133 NPR/USD is
    /// 1.66 NPR, not 1.66 by luck but 1.6625 truncated, and that error then multiplies by every
    /// quantity ever received. The scales must match the column, so this rounds to
    /// <see cref="UnitCostScale"/>.
    /// </summary>
    public static decimal ToBaseUnitCost(decimal transactionUnitCost, decimal exchangeRate) =>
        Math.Round(transactionUnitCost * exchangeRate, UnitCostScale, MidpointRounding.AwayFromZero);

    /// <summary>Scale of a stored unit cost -- see <see cref="ToBaseUnitCost"/>. Mirrors the
    /// precision of every <c>UnitCost</c> column in the Inventory schema.</summary>
    public const int UnitCostScale = 4;

    /// <summary>Normalises a user-entered rate to <see cref="RateScale"/> so the value stored on a
    /// document is the same value every later conversion uses.</summary>
    public static decimal NormaliseRate(decimal exchangeRate) =>
        Math.Round(exchangeRate, RateScale, MidpointRounding.AwayFromZero);

    /// <summary>
    /// Guard shared by every aggregate that carries a currency, so the same two invariants hold on
    /// all twelve of them: the rate is strictly positive, and a base-currency document's rate is
    /// exactly one. The second is what the reference product enforces in its UI by disabling the
    /// Exchange Rate input and pinning it to 1 whenever the selected currency is NPR (confirmed
    /// live 2026-09-04, on both the Invoice and the Customer Payment form) -- here it is an
    /// invariant of the aggregate rather than a property of one entry path, the same call
    /// <c>Invoice.SetExport</c> makes about zero-rating an export sale.
    /// </summary>
    public static (string CurrencyCode, decimal ExchangeRate) Validate(string? currencyCode, decimal? exchangeRate)
    {
        var code = string.IsNullOrWhiteSpace(currencyCode)
            ? CurrencyCatalog.BaseCode
            : currencyCode.Trim().ToUpperInvariant();

        if (!CurrencyCatalog.Contains(code))
        {
            throw new InvalidOperationException($"'{code}' is not a currency this product supports.");
        }

        var rate = exchangeRate ?? BaseRate;

        if (rate <= 0)
        {
            throw new InvalidOperationException("A document's Exchange Rate must be greater than zero.");
        }

        if (CurrencyCatalog.IsBase(code) && rate != BaseRate)
        {
            throw new InvalidOperationException(
                $"A document in {CurrencyCatalog.BaseCode} must have an Exchange Rate of exactly 1.");
        }

        return (code, NormaliseRate(rate));
    }
}
