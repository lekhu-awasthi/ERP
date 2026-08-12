using ErpApp.Application.Accounting.Posting;
using ErpApp.Domain.Accounting;

namespace ErpApp.Application.Sales.Posting;

/// <summary>Exact reverse of InvoicePostingRule: Credit Accounts Receivable for the grand total,
/// Debit each line's Sales Revenue account, Debit VAT Payable for the summed VAT.</summary>
public sealed class CreditNotePostingRule : IGlPostingRule<CreditNotePostingInput>
{
    public IReadOnlyList<GlLineInput> BuildLines(CreditNotePostingInput document)
    {
        var lines = new List<GlLineInput>
        {
            new(document.AccountsReceivableAccountId, 0, document.Lines.Sum(x => x.Amount + x.VatAmount)),
        };

        lines.AddRange(document.Lines
            .GroupBy(x => x.SalesAccountId)
            .Select(g => new GlLineInput(g.Key, g.Sum(x => x.Amount), 0)));

        var totalVat = document.Lines.Sum(x => x.VatAmount);
        if (totalVat > 0)
        {
            lines.Add(new GlLineInput(document.VatPayableAccountId, totalVat, 0));
        }

        return lines;
    }
}
