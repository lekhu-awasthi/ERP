using ErpApp.Application.Accounting.Posting;
using ErpApp.Domain.Accounting;

namespace ErpApp.Application.Sales.Posting;

/// <summary>
/// Debit Accounts Receivable for the grand total; Credit each line's Sales Revenue account for
/// its pre-VAT Amount (grouped so two lines sharing a Sales Account don't produce two separate GL
/// lines); Credit VAT Payable for the summed VatAmount (omitted entirely if zero -- an all-NoVat/
/// ZeroVat invoice has no VAT leg). Reuses GlJournalEntry.Post's balanced-invariant check, same
/// as every other IGlPostingRule.
///
/// Phase 7: when CogsAmount is greater than zero (at least one Goods line was actually consumed
/// from stock), also Debit CogsAccountId / Credit InventoryAccountId for that amount -- a second,
/// independently-balanced pair appended to the revenue/AR/VAT lines above, so the combined entry
/// stays balanced by construction (see InvoicePostingInput's doc comment for where CogsAmount
/// comes from).
/// </summary>
public sealed class InvoicePostingRule : IGlPostingRule<InvoicePostingInput>
{
    public IReadOnlyList<GlLineInput> BuildLines(InvoicePostingInput document)
    {
        var lines = new List<GlLineInput>
        {
            new(document.AccountsReceivableAccountId, document.Lines.Sum(x => x.Amount + x.VatAmount), 0),
        };

        lines.AddRange(document.Lines
            .GroupBy(x => x.SalesAccountId)
            .Select(g => new GlLineInput(g.Key, 0, g.Sum(x => x.Amount))));

        var totalVat = document.Lines.Sum(x => x.VatAmount);
        if (totalVat > 0)
        {
            lines.Add(new GlLineInput(document.VatPayableAccountId, 0, totalVat));
        }

        if (document.CogsAmount > 0 && document.CogsAccountId is { } cogsAccountId && document.InventoryAccountId is { } inventoryAccountId)
        {
            lines.Add(new GlLineInput(cogsAccountId, document.CogsAmount, 0));
            lines.Add(new GlLineInput(inventoryAccountId, 0, document.CogsAmount));
        }

        return lines;
    }
}
