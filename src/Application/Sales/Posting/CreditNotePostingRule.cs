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
        var receivable = document.Lines.Sum(x => x.Amount + x.ServiceChargeAmount + x.VatAmount) + document.RoundOff;

        var lines = new List<GlLineInput>
        {
            new(document.AccountsReceivableAccountId, 0, receivable),
        };

        lines.AddRange(document.Lines
            .GroupBy(x => x.SalesAccountId)
            .Select(g => new GlLineInput(g.Key, g.Sum(x => x.Amount), 0)));

        // Phase 63 -- the reverse of InvoicePostingRule's two till legs. Both are zero on an ERP note,
        // so its entry is unchanged.
        var totalServiceCharge = document.Lines.Sum(x => x.ServiceChargeAmount);
        if (totalServiceCharge > 0)
        {
            lines.Add(new GlLineInput(
                document.ServiceChargeAccountId
                    ?? throw new InvalidOperationException("A service charge needs its account resolved before posting."),
                totalServiceCharge, 0));
        }

        if (document.RoundOff != 0)
        {
            var roundingAccountId = document.RoundingAccountId
                ?? throw new InvalidOperationException("A round-off needs its account resolved before posting.");

            lines.Add(document.RoundOff > 0
                ? new GlLineInput(roundingAccountId, document.RoundOff, 0)
                : new GlLineInput(roundingAccountId, 0, -document.RoundOff));
        }

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
