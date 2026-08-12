using ErpApp.Application.Accounting.Posting;
using ErpApp.Domain.Accounting;

namespace ErpApp.Application.Purchasing.Posting;

/// <summary>Exact reverse of PurchaseBillPostingRule's non-TDS legs: Debit Accounts Payable for
/// the grand total, Credit each line's Purchase Account, Credit VAT Receivable for the summed
/// VAT.</summary>
public sealed class DebitNotePostingRule : IGlPostingRule<DebitNotePostingInput>
{
    public IReadOnlyList<GlLineInput> BuildLines(DebitNotePostingInput document)
    {
        var lines = new List<GlLineInput>
        {
            new(document.AccountsPayableAccountId, document.Lines.Sum(x => x.Amount + x.VatAmount), 0),
        };

        lines.AddRange(document.Lines
            .GroupBy(x => x.PurchaseAccountId)
            .Select(g => new GlLineInput(g.Key, 0, g.Sum(x => x.Amount))));

        var totalVat = document.Lines.Sum(x => x.VatAmount);
        if (totalVat > 0)
        {
            lines.Add(new GlLineInput(document.VatReceivableAccountId, 0, totalVat));
        }

        return lines;
    }
}
