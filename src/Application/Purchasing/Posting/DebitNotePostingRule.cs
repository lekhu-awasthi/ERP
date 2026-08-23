using ErpApp.Application.Accounting.Posting;
using ErpApp.Domain.Accounting;

namespace ErpApp.Application.Purchasing.Posting;

/// <summary>Exact reverse of PurchaseBillPostingRule, TDS leg included: Debit Accounts Payable for
/// the grand total minus TdsAmount, Debit TDS Payable for TdsAmount (omitted if zero), Credit each
/// line's resolved account (Inventory for a Goods line, Purchase Expense for a Service line),
/// Credit VAT Receivable for the summed VAT. A full reversal (a DebitNote whose lines/TdsAmount
/// exactly match the source PurchaseBill) nets every account -- including TDS Payable -- back to
/// zero.</summary>
public sealed class DebitNotePostingRule : IGlPostingRule<DebitNotePostingInput>
{
    public IReadOnlyList<GlLineInput> BuildLines(DebitNotePostingInput document)
    {
        var grandTotal = document.Lines.Sum(x => x.Amount + x.VatAmount);

        var lines = new List<GlLineInput>
        {
            new(document.AccountsPayableAccountId, grandTotal - document.TdsAmount, 0),
        };

        if (document.TdsAmount > 0)
        {
            lines.Add(new GlLineInput(document.TdsPayableAccountId, document.TdsAmount, 0));
        }

        lines.AddRange(document.Lines
            .GroupBy(x => x.DebitAccountId)
            .Select(g => new GlLineInput(g.Key, 0, g.Sum(x => x.Amount))));

        var totalVat = document.Lines.Sum(x => x.VatAmount);
        if (totalVat > 0)
        {
            lines.Add(new GlLineInput(document.VatReceivableAccountId, 0, totalVat));
        }

        return lines;
    }
}
