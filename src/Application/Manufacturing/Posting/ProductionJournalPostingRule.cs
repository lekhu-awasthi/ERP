using ErpApp.Application.Accounting.Posting;
using ErpApp.Domain.Accounting;

namespace ErpApp.Application.Manufacturing.Posting;

/// <summary>
/// Worked out on paper first and then checked against the live reference product, per phase-7's
/// InventoryAdjustment discipline and phase-6 bug #3's warning that a rule can satisfy its own
/// sum(Debit)==sum(Credit) while leaving a paired account permanently unbalanced.
///
/// <para><b>What the reference product does: nothing.</b> erp-module-scan.md left this as an open
/// item ("likely emits GL Transactions -- unconfirmed which accounts"). The 2026-09-02 pass closed
/// it by experiment: a Production Journal was created and approved (PJ0008, 02-09-2026), and it
/// does not appear anywhere in a 199-row Journal report covering that exact date, while its stock
/// moved (the raw material's on-hand went 8896.5 -> 8899.5, being -12 consumed +15 by-product).
/// Production is also absent from the Transaction list report's own type list, which does include
/// Inventory Adjustment and Warehouse Transfer. That tenant runs <i>periodic</i> inventory -- its
/// Purchase Bills debit "Purchase Goods", a Direct Expense -- under which a production journal
/// genuinely has nothing to post, because the raw material was expensed at purchase.</para>
///
/// <para><b>Why we post anyway.</b> This codebase is <i>perpetual</i>: since the post-Phase-19 fix
/// (phase-7-status.md's addendum) a Goods PurchaseBill debits TenantSettings.
/// DefaultInventoryAccountId, so the Inventory account is a real asset balance that is supposed to
/// track the FIFO ledger. Posting nothing would leave that balance understating stock by exactly
/// the production expenses capitalised into finished goods, silently and forever. So this is a
/// deliberate, reasoned divergence, not parity by accident.</para>
///
/// <para><b>The entry, posted gross rather than netted</b> so the Journal report shows the real
/// transformation instead of a single unexplained figure:</para>
/// <list type="bullet">
/// <item>Debit Inventory with FinishedGoodsValue + ByProductValue (the new layers).</item>
/// <item>Credit Inventory with RawMaterialCost (the layers consumed).</item>
/// <item>Credit Production Cost with the difference, when non-zero.</item>
/// </list>
///
/// <para><b>Net effect, traced per account.</b> Inventory nets to
/// <c>(Finished + ByProduct) - RawMaterial</c>, which is exactly the production expenses
/// capitalised into stock (plus/minus the sub-cent rounding residue) -- not zero, and not the full
/// raw-material value. Production Cost nets to a credit of that same figure: a contra-expense
/// absorbing into inventory the labour/overhead the tenant already booked to real expense accounts
/// through Expense/PurchaseBill documents. Nothing else is touched. When the finished good is
/// later sold, InvoicePostingRule's COGS leg debits COGS and credits Inventory at the FIFO cost
/// computed here, so the expense reaches the P&amp;L at the point of sale rather than the point of
/// production -- which is the entire point of capitalising it.</para>
///
/// <para>sum(Debit) = Finished + ByProduct. sum(Credit) = RawMaterial + (Finished + ByProduct -
/// RawMaterial) = Finished + ByProduct. Balanced <b>by construction</b>, for any inputs, including
/// the degenerate expense-free case where the two Inventory legs are equal and the Production Cost
/// leg is omitted entirely.</para>
/// </summary>
public sealed class ProductionJournalPostingRule : IGlPostingRule<ProductionJournalPostingInput>
{
    public IReadOnlyList<GlLineInput> BuildLines(ProductionJournalPostingInput document)
    {
        var stockCreated = document.FinishedGoodsValue + document.ByProductValue;
        var productionCost = stockCreated - document.RawMaterialCost;

        var lines = new List<GlLineInput>();

        if (stockCreated > 0)
        {
            lines.Add(new GlLineInput(document.InventoryAccountId, stockCreated, 0));
        }

        if (document.RawMaterialCost > 0)
        {
            lines.Add(new GlLineInput(document.InventoryAccountId, 0, document.RawMaterialCost));
        }

        // Normally a credit (expenses absorbed into stock). A debit only when the raw material
        // consumed was worth more than the stock created, which happens when production expenses
        // are zero and the finished unit cost rounded down -- a sub-cent figure, but it still has
        // to go somewhere for the entry to balance.
        if (productionCost > 0)
        {
            lines.Add(new GlLineInput(document.ProductionCostAccountId, 0, productionCost));
        }
        else if (productionCost < 0)
        {
            lines.Add(new GlLineInput(document.ProductionCostAccountId, -productionCost, 0));
        }

        return lines;
    }
}
