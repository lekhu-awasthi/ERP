using ErpApp.Application.Accounting.Posting;
using ErpApp.Domain.Accounting;

namespace ErpApp.Application.Purchasing.Posting;

/// <summary>
/// Debit each line's resolved account for its pre-VAT Amount (Inventory for a Goods line, Purchase
/// Expense for a Service line -- see PurchaseBillAccountResolver; grouped so two lines sharing an
/// account don't produce two separate GL lines); Debit VAT Receivable for the summed
/// VatAmount (omitted if zero); Credit TDS Payable for TdsAmount (omitted if zero -- TDS is
/// withheld from the amount owed, not a separate payment); Credit Accounts Payable for the grand
/// total minus TdsAmount (TDS reduces the AP credit rather than being a separate line -- the
/// scope decision this phase made: withholding TDS means less cash will ultimately move to the
/// supplier, so the payable itself is smaller by that amount, while a separate TDS Payable
/// liability is owed to the government instead. See phase-6-status.md's scope decisions for the
/// reasoning and the alternative considered.) Reuses GlJournalEntry.Post's balanced-invariant
/// check, same as every other IGlPostingRule.
///
/// <para><b>Phase 29 (FR-6.15), the landed-cost pair.</b> When the bill carries an Additional Cost
/// section, one further Debit Inventory / Credit Landed Cost Clearing pair is appended for
/// <see cref="PurchaseBillPostingInput.CapitalisedAdditionalCost"/> -- posted gross as its own
/// Inventory line rather than folded into the goods debit, so the Journal report shows the landed
/// cost as the distinct thing it is (phase-25's precedent for posting a transformation gross).</para>
///
/// <para><b>Why a clearing account, and why we post at all.</b> Confirmed live 2026-09-04 on two
/// already-approved reference bills carrying 900 and 1,800 of additional cost: neither figure
/// reaches the general ledger at all, neither is in the bill's Grand Total, and the supplier is
/// credited the goods total only -- and an Additional Cost row has no payee field to name anyone
/// else with. That tenant is <i>periodic</i> (its Goods lines debit "Purchase Goods", a Direct
/// Expense), so its landed cost genuinely has nowhere in the GL to go and lives only in its
/// stock-costing subsystem -- where it is fully capitalised: <c>SSSS (P0597)</c> shows In
/// 100 @ 209 = 20,900 for a bill of 100 @ 200 plus 900 of additional cost. We are <i>perpetual</i>,
/// so the Inventory account is supposed to track the FIFO ledger; posting nothing would leave it
/// understating stock by exactly the capitalised cost, permanently. This is phase-25 Decision A's
/// argument, unchanged. The credit therefore cannot be the supplier (live: it isn't) and cannot be
/// a payee (live: there is none), so it is a clearing liability the freight vendor's own bill later
/// clears -- see TenantSettings.DefaultLandedCostClearingAccountId.</para>
///
/// <para><b>Net effect, traced per account</b> (phase-6 bug #3's discipline). Inventory receives
/// the goods amounts plus the capitalised cost, which is exactly the value of the FIFO layers this
/// approval created, so the account and the ledger agree to the paisa. Landed Cost Clearing is
/// credited that same figure and nothing else; it nets to zero once the carrier's own bill is
/// entered against it. Accounts Payable, VAT Receivable and TDS Payable are untouched by this
/// phase -- the supplier is owed the goods total and not a paisa more.</para>
/// </summary>
public sealed class PurchaseBillPostingRule : IGlPostingRule<PurchaseBillPostingInput>
{
    public IReadOnlyList<GlLineInput> BuildLines(PurchaseBillPostingInput document)
    {
        var grandTotal = document.Lines.Sum(x => x.Amount + x.VatAmount);

        var lines = new List<GlLineInput>();

        lines.AddRange(document.Lines
            .GroupBy(x => x.DebitAccountId)
            .Select(g => new GlLineInput(g.Key, g.Sum(x => x.Amount), 0)));

        var totalVat = document.Lines.Sum(x => x.VatAmount);
        if (totalVat > 0)
        {
            lines.Add(new GlLineInput(document.VatReceivableAccountId, totalVat, 0));
        }

        if (document.TdsAmount > 0)
        {
            lines.Add(new GlLineInput(document.TdsPayableAccountId, 0, document.TdsAmount));
        }

        lines.Add(new GlLineInput(document.AccountsPayableAccountId, 0, grandTotal - document.TdsAmount));

        // Phase 29. Balanced by construction whichever way the figure points; negative only when
        // unit-cost rounding took the layers slightly below the goods amounts, a sub-paisa case that
        // still has to go somewhere for the entry to balance (phase-25's ProductionCost leg).
        if (document.CapitalisedAdditionalCost != 0
            && document.InventoryAccountId is { } inventoryAccountId
            && document.LandedCostClearingAccountId is { } clearingAccountId)
        {
            var capitalised = document.CapitalisedAdditionalCost;
            if (capitalised > 0)
            {
                lines.Add(new GlLineInput(inventoryAccountId, capitalised, 0));
                lines.Add(new GlLineInput(clearingAccountId, 0, capitalised));
            }
            else
            {
                lines.Add(new GlLineInput(inventoryAccountId, 0, -capitalised));
                lines.Add(new GlLineInput(clearingAccountId, -capitalised, 0));
            }
        }

        return lines;
    }
}
