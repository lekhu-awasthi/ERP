using ErpApp.Application.Accounting.Posting;
using ErpApp.Domain.Accounting;

namespace ErpApp.Application.Sales.Posting;

/// <summary>Exact reverse of InvoicePostingRule: Credit Accounts Receivable for the grand total,
/// Debit each line's Sales Revenue account, Debit VAT Payable for the summed VAT.
///
/// Post-Phase-7 fix: when CogsAmount is greater than zero (the source Invoice actually consumed
/// stock for at least one matching line), also Debit InventoryAccountId / Credit CogsAccountId for
/// that amount -- the exact reverse of InvoicePostingRule's own COGS leg -- so a full-reversal
/// CreditNote nets both Inventory and COGS back to zero, not just Accounts Receivable/Sales/VAT.
/// See CreditNotePostingInput's doc comment.</summary>
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

        if (document.CogsAmount > 0 && document.CogsAccountId is { } cogsAccountId && document.InventoryAccountId is { } inventoryAccountId)
        {
            lines.Add(new GlLineInput(inventoryAccountId, document.CogsAmount, 0));
            lines.Add(new GlLineInput(cogsAccountId, 0, document.CogsAmount));
        }

        return lines;
    }
}
