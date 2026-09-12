using ErpApp.Application.Accounting.Posting;
using ErpApp.Application.Common.Exceptions;
using ErpApp.Domain.Accounting;

namespace ErpApp.Application.Purchasing.Posting;

/// <summary>Reverse of PurchaseBillPostingRule, TDS leg included: Debit Accounts Payable for the
/// grand total minus TdsAmount, Debit TDS Payable for TdsAmount (omitted if zero), Credit each
/// Service line's resolved Purchase Expense account, Credit VAT Receivable for the summed VAT. A
/// full reversal (a DebitNote whose lines/TdsAmount exactly match the source PurchaseBill) nets
/// every account -- including TDS Payable -- back to zero.
///
/// <para><b>Phase 37 -- the Goods lines no longer credit Inventory their return price.</b> A return
/// relieves the FIFO ledger at the cost the units were <i>consumed</i> at, which is the oldest
/// layers' landed cost and has nothing to do with the price the supplier is crediting back. Phase 6
/// modelled the two as the same number and phase 29 patched the freight half of the difference;
/// what was left is the price half, and it is the same trap phase-36 named for settlements: a
/// relieving amount folds at the rate of <i>the thing being relieved</i>. So Inventory is credited
/// <c>RelievedInventoryCost</c> -- what left the ledger -- and the Landed Cost Clearing account is
/// still debited its released share, which is what unwinds the carrier's accrual.</para>
///
/// <para><b>The plug is the whole point, not a rounding sink.</b> Debits (the supplier's credit note
/// plus the clearing unwind) and credits (what the goods actually cost) are two different
/// quantities once the return price and the FIFO cost differ, and the gap is a real gain or loss on
/// the return. It is derived here as the sum of every other line -- never as a separately computed
/// figure -- which is the only construction that keeps <c>sum(Debit) == sum(Credit)</c> under the
/// currency fold (phase 28) and absorbs the rounding residue at the same time. It posts to the
/// tenant's Inventory Adjustment account, the same account every other "inventory is worth
/// something other than the documents say" entry uses.</para>
/// </summary>
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

        // A standalone note (no source bill, so nothing was consumed) keeps the pre-phase-37
        // behaviour exactly: every line credits its own resolved account at its own amount.
        var creditsStockLinesAtCost = document.RelievedInventoryCost is not null;

        lines.AddRange(document.Lines
            .Where(x => !creditsStockLinesAtCost || !x.RelievesStock)
            .GroupBy(x => x.DebitAccountId)
            .Select(g => new GlLineInput(g.Key, 0, g.Sum(x => x.Amount))));

        var totalVat = document.Lines.Sum(x => x.VatAmount);
        if (totalVat > 0)
        {
            lines.Add(new GlLineInput(document.VatReceivableAccountId, 0, totalVat));
        }

        if (document.RelievedInventoryCost is { } relieved && relieved > 0
            && document.InventoryAccountId is { } inventoryAccountId)
        {
            lines.Add(new GlLineInput(inventoryAccountId, 0, relieved));
        }

        // Phase 29 (FR-6.15) -- the carrier's accrual is unwound by the share of the source bill's
        // capitalised Additional Cost that the returned quantities carried, which nets the clearing
        // account back to zero across a full return exactly as Accounts Payable and TDS Payable
        // already do. Its matching Inventory credit is no longer a separate leg: from phase 37 the
        // single Inventory credit above is the layers' full landed cost, freight included, so a
        // second credit here would relieve the freight twice.
        if (document.ReleasedAdditionalCost > 0 && document.LandedCostClearingAccountId is { } clearingAccountId)
        {
            lines.Add(new GlLineInput(clearingAccountId, document.ReleasedAdditionalCost, 0));
        }

        var balance = lines.Sum(x => x.Debit) - lines.Sum(x => x.Credit);
        if (balance != 0)
        {
            if (document.InventoryAdjustmentAccountId is not { } adjustmentAccountId)
            {
                throw new ConflictException(
                    "This return credits a different amount than the goods cost, and no Default Inventory Adjustment " +
                    "account is configured to take the difference. Set it under Accounting Defaults before approving.");
            }

            lines.Add(balance > 0
                ? new GlLineInput(adjustmentAccountId, 0, balance)
                : new GlLineInput(adjustmentAccountId, -balance, 0));
        }

        return lines;
    }
}
