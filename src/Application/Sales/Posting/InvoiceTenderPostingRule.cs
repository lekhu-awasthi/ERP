using ErpApp.Application.Accounting.Posting;
using ErpApp.Domain.Accounting;

namespace ErpApp.Application.Sales.Posting;

/// <summary>One tender's account and amount, as frozen on the invoice.</summary>
public sealed record InvoiceTenderPostingLine(Guid AccountId, decimal Amount);

/// <summary>
/// Phase 61 -- what a till sale's tenders settled: the input of the <b>second</b> GL entry posted
/// against the same invoice.
/// </summary>
/// <param name="ChangeAccountId">The drawer's account, which the change comes out of. Null only when
/// no change was given.</param>
public sealed record InvoiceTenderPostingInput(
    Guid AccountsReceivableAccountId,
    IReadOnlyList<InvoiceTenderPostingLine> Tenders,
    Guid? ChangeAccountId,
    decimal ChangeAmount);

/// <summary>
/// Phase 61 (phase 59 Decision D) -- debit each tender's account for what was handed over in it,
/// credit the change back out of the drawer, and credit Accounts Receivable for the difference:
/// the amount the tenders settled.
///
/// <para><b>Why a second entry, not more lines in the sale's.</b> The vendor posts the sale and its
/// settlement as one entry (INV0002: Dr Cash In Hand 317, Dr Cash Customer 317, Cr Sales…, Cr Cash
/// Customer 317). Kept apart, the sale entry is byte-for-byte what an ERP invoice of the same lines
/// posts -- every report that reads a sale reads it the same whichever channel raised it -- and the
/// settlement entry is byte-for-byte what a receipt allocated in full would have posted. Phase 36
/// already made "one entry per document" a habit rather than an invariant, so a Void reverses both
/// through <c>SourceDocumentGlEntries</c> with no new code.</para>
///
/// <para>Tenders sharing an account are summed into one line, and the drawer's change is netted into
/// the cash tender's line rather than posted as its own pair: a 500 note for a 317 bill is 317 into
/// the drawer, which is what the drawer's account should show. A tender netted to zero by change
/// (cash 20 handed over and 20 given back) leaves no line at all.</para>
/// </summary>
public sealed class InvoiceTenderPostingRule : IGlPostingRule<InvoiceTenderPostingInput>
{
    public IReadOnlyList<GlLineInput> BuildLines(InvoiceTenderPostingInput document)
    {
        var debits = document.Tenders
            .GroupBy(x => x.AccountId)
            .ToDictionary(g => g.Key, g => g.Sum(x => x.Amount));

        if (document.ChangeAmount > 0)
        {
            var changeAccountId = document.ChangeAccountId
                ?? throw new InvalidOperationException("Change needs the drawer's account to come out of.");
            debits[changeAccountId] = debits.GetValueOrDefault(changeAccountId) - document.ChangeAmount;
        }

        var settled = debits.Values.Sum();

        var lines = debits
            .Where(x => x.Value != 0)
            .Select(x => x.Value > 0
                ? new GlLineInput(x.Key, x.Value, 0)
                : new GlLineInput(x.Key, 0, -x.Value))
            .ToList();

        if (settled != 0)
        {
            lines.Add(new GlLineInput(document.AccountsReceivableAccountId, 0, settled));
        }

        return lines;
    }
}
