using ErpApp.Application.Accounting.Posting;
using ErpApp.Domain.Accounting;

namespace ErpApp.Application.Purchasing.Posting;

/// <summary>
/// Debit each line's resolved account for its pre-VAT Amount (Inventory for a Goods line, Purchase
/// Expense for a Service line -- see PurchaseBillAccountResolver; grouped so two lines sharing an
/// account don't produce two separate GL lines); Debit VAT Receivable for the summed
/// VatAmount (omitted if zero); Credit TDS Payable for TdsAmount (omitted if zero -- TDS is
/// withheld from the amount owed, not a separate payment); Credit Accounts Payable for the grand
/// total minus TdsAmount (TDS reduces the AP credit rather than being a separate line -- the
/// scope decision this phase made: withholding TDS means less cash will ultimately move to the
/// supplier, so the payable itself is smaller by that amount, while a separate TDS Payable
/// liability is owed to the government instead. See phase-6-status.md's scope decisions for the
/// reasoning and the alternative considered.) Reuses GlJournalEntry.Post's balanced-invariant
/// check, same as every other IGlPostingRule.
/// </summary>
public sealed class PurchaseBillPostingRule : IGlPostingRule<PurchaseBillPostingInput>
{
    public IReadOnlyList<GlLineInput> BuildLines(PurchaseBillPostingInput document)
    {
        var grandTotal = document.Lines.Sum(x => x.Amount + x.VatAmount);

        var lines = new List<GlLineInput>();

        lines.AddRange(document.Lines
            .GroupBy(x => x.DebitAccountId)
            .Select(g => new GlLineInput(g.Key, g.Sum(x => x.Amount), 0)));

        var totalVat = document.Lines.Sum(x => x.VatAmount);
        if (totalVat > 0)
        {
            lines.Add(new GlLineInput(document.VatReceivableAccountId, totalVat, 0));
        }

        if (document.TdsAmount > 0)
        {
            lines.Add(new GlLineInput(document.TdsPayableAccountId, 0, document.TdsAmount));
        }

        lines.Add(new GlLineInput(document.AccountsPayableAccountId, 0, grandTotal - document.TdsAmount));

        return lines;
    }
}
