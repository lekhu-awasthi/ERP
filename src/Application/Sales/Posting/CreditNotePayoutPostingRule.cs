using ErpApp.Application.Accounting.Posting;
using ErpApp.Domain.Accounting;

namespace ErpApp.Application.Sales.Posting;

/// <summary>Phase 63 -- what a till refund handed back: the input of the <b>second</b> GL entry posted
/// against the credit note, the mirror of <see cref="InvoiceTenderPostingInput"/>.</summary>
public sealed record CreditNotePayoutPostingInput(
    Guid AccountsReceivableAccountId,
    IReadOnlyList<InvoiceTenderPostingLine> Payouts);

/// <summary>
/// Phase 63 -- credit each payout's account for what was handed back in it, and debit Accounts
/// Receivable for the total: the credit note paying itself out, exactly as the sale's tenders are the
/// invoice paying itself (<see cref="InvoiceTenderPostingRule"/>). Kept apart from the credit note's own
/// entry for that rule's reason -- the note's entry is byte-for-byte what an ERP credit note of the same
/// lines posts, and a Void reverses both through <c>SourceDocumentGlEntries</c> with no new code.
///
/// <para>A cash payout credits the drawer's account, so the drawer's expected cash falls by it; payouts
/// sharing an account are summed into one line.</para>
/// </summary>
public sealed class CreditNotePayoutPostingRule : IGlPostingRule<CreditNotePayoutPostingInput>
{
    public IReadOnlyList<GlLineInput> BuildLines(CreditNotePayoutPostingInput document)
    {
        var credits = document.Payouts
            .GroupBy(x => x.AccountId)
            .Select(g => new { AccountId = g.Key, Amount = g.Sum(x => x.Amount) })
            .Where(x => x.Amount != 0)
            .ToList();

        var total = credits.Sum(x => x.Amount);
        if (total == 0)
        {
            return [];
        }

        var lines = new List<GlLineInput> { new(document.AccountsReceivableAccountId, total, 0) };
        lines.AddRange(credits.Select(x => new GlLineInput(x.AccountId, 0, x.Amount)));
        return lines;
    }
}
