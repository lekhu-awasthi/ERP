using ErpApp.Application.Accounting.Posting;
using ErpApp.Domain.Accounting;

namespace ErpApp.Application.Sales.Posting;

/// <summary>
/// Debit Accounts Receivable for the grand total; Credit each line's Sales Revenue account for
/// its pre-VAT Amount (grouped so two lines sharing a Sales Account don't produce two separate GL
/// lines); Credit VAT Payable for the summed VatAmount (omitted entirely if zero -- an all-NoVat/
/// ZeroVat invoice has no VAT leg). Reuses GlJournalEntry.Post's balanced-invariant check, same
/// as every other IGlPostingRule.
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

        return lines;
    }
}
