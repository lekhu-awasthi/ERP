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
///
/// <para><b>Phase 61 -- the till sale's legs.</b> Accounts Receivable is debited for everything the
/// customer owes: each line's amount, service charge and VAT, plus the round-off. The service charge
/// is credited to its own account (summed, one GL line), and the round-off to the rounding account --
/// credited when it adds to the bill, debited when it takes off. Both are zero on an ERP invoice, so
/// that entry is unchanged. How the sale was <i>paid</i> is not here: tenders post as a second entry
/// against the same invoice (<see cref="InvoiceTenderPostingRule"/>, phase 59 Decision D).</para>
/// </summary>
public sealed class InvoicePostingRule : IGlPostingRule<InvoicePostingInput>
{
    public IReadOnlyList<GlLineInput> BuildLines(InvoicePostingInput document)
    {
        var receivable = document.Lines.Sum(x => x.Amount + x.ServiceChargeAmount + x.VatAmount) + document.RoundOff;

        var lines = new List<GlLineInput>
        {
            new(document.AccountsReceivableAccountId, receivable, 0),
        };

        lines.AddRange(document.Lines
            .GroupBy(x => x.SalesAccountId)
            .Select(g => new GlLineInput(g.Key, 0, g.Sum(x => x.Amount))));

        var totalServiceCharge = document.Lines.Sum(x => x.ServiceChargeAmount);
        if (totalServiceCharge > 0)
        {
            lines.Add(new GlLineInput(
                document.ServiceChargeAccountId
                    ?? throw new InvalidOperationException("A service charge needs its account resolved before posting."),
                0, totalServiceCharge));
        }

        var totalVat = document.Lines.Sum(x => x.VatAmount);
        if (totalVat > 0)
        {
            lines.Add(new GlLineInput(document.VatPayableAccountId, 0, totalVat));
        }

        if (document.RoundOff != 0)
        {
            var roundingAccountId = document.RoundingAccountId
                ?? throw new InvalidOperationException("A round-off needs its account resolved before posting.");

            lines.Add(document.RoundOff > 0
                ? new GlLineInput(roundingAccountId, 0, document.RoundOff)
                : new GlLineInput(roundingAccountId, -document.RoundOff, 0));
        }

        if (document.CogsAmount > 0 && document.CogsAccountId is { } cogsAccountId && document.InventoryAccountId is { } inventoryAccountId)
        {
            lines.Add(new GlLineInput(cogsAccountId, document.CogsAmount, 0));
            lines.Add(new GlLineInput(inventoryAccountId, 0, document.CogsAmount));
        }

        return lines;
    }
}
