using ErpApp.Application.Common.Persistence;
using ErpApp.Domain.Accounting;
using ErpApp.Domain.Common;

namespace ErpApp.Application.Accounting.Posting;

/// <summary>
/// Converts an already-built <see cref="GlLineInput"/> list from a document's own currency into
/// the base currency -- the fallback conversion path, used by exactly the two posting rules whose
/// input <i>is</i> the domain aggregate (JournalVoucher and CashTransfer, see their rules'
/// doc comments) and which therefore have no line-level amounts a handler could convert first.
///
/// <para><b>Every other document type converts earlier, and this is the important distinction.</b>
/// Invoice, CreditNote, PurchaseBill, Expense, DebitNote and Payment all go through an account
/// resolver that takes line amounts as arguments, so their handlers convert those arguments and
/// the posting rule then derives its balancing leg (AR, AP, the cash side) as a sum of numbers that
/// are <i>already</i> base currency. Those entries balance by construction and can never produce a
/// residue. Converting a finished GlLineInput list cannot offer that guarantee, because the
/// balancing leg gets rounded independently of the legs it balances -- for instance two 0.05 debits
/// against one 0.10 credit at rate 1.5 give 0.08 + 0.08 = 0.16 against 0.15.</para>
///
/// <para><b>So the residue is booked, not absorbed</b> (the phase-25 rule: name the residue). It is
/// a genuine, if tiny, exchange difference and it goes to the tenant's Forex Gain/Loss account like
/// any other. It is bounded by half a paisa per line, and it is exactly zero for the overwhelming
/// majority of entries -- in which case no forex leg is added and no forex account is required,
/// which is what keeps a base-currency (rate 1) document on a completely unchanged code path.</para>
/// </summary>
internal static class GlCurrencyConversion
{
    public static async Task<IReadOnlyList<GlLineInput>> ToBaseAsync(
        IAppDbContext db,
        Guid organizationId,
        IReadOnlyList<GlLineInput> lines,
        decimal exchangeRate,
        CancellationToken cancellationToken)
    {
        if (exchangeRate == ExchangeRates.BaseRate)
        {
            return lines;
        }

        var converted = lines
            .Select(x => new GlLineInput(
                x.AccountId,
                ExchangeRates.ToBase(x.Debit, exchangeRate),
                ExchangeRates.ToBase(x.Credit, exchangeRate)))
            .ToList();

        var debitTotal = converted.Sum(x => x.Debit);
        var creditTotal = converted.Sum(x => x.Credit);
        var residue = creditTotal - debitTotal;

        if (residue == 0)
        {
            return converted;
        }

        // Sign convention (shared with ForexPostingRule): a credit-heavy entry needs a balancing
        // debit, which is a loss; a debit-heavy entry needs a balancing credit, which is a gain.
        var forexAccountId = await ForexAccountResolver.ResolveAsync(db, organizationId, -residue, cancellationToken);

        converted.Add(residue > 0
            ? new GlLineInput(forexAccountId, residue, 0)
            : new GlLineInput(forexAccountId, 0, -residue));

        return converted;
    }
}
