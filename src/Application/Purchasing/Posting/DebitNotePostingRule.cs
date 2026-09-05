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

        // Phase 29 (FR-6.15) -- the mirror of PurchaseBillPostingRule's landed-cost pair, and the
        // reason it cannot be left out. Returning goods removes their FIFO layers at the cost those
        // layers actually carry, capitalised additional cost included; crediting Inventory only the
        // return price would leave the account permanently above the ledger by exactly the freight
        // and duty sitting in the returned units. This is phase-6 bug #3's trap in a new place, so
        // the released share goes back out of Inventory and back into the clearing account it came
        // from -- which nets to zero across a full return, exactly as Accounts Payable and TDS
        // Payable already do.
        if (document.ReleasedAdditionalCost > 0
            && document.InventoryAccountId is { } inventoryAccountId
            && document.LandedCostClearingAccountId is { } clearingAccountId)
        {
            lines.Add(new GlLineInput(clearingAccountId, document.ReleasedAdditionalCost, 0));
            lines.Add(new GlLineInput(inventoryAccountId, 0, document.ReleasedAdditionalCost));
        }

        return lines;
    }
}
