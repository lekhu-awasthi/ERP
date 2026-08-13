using ErpApp.Application.Accounting.Posting;
using ErpApp.Domain.Accounting;

namespace ErpApp.Application.Inventory.Posting;

/// <summary>
/// Worked out on paper first, per phase-6-status.md bug #3's lesson about paired-effect documents
/// (don't trust "this rule's own entry balances" alone -- trace the *net* effect on every account
/// it touches). A single InventoryAdjustment can carry both Increase and Decrease lines at once, so
/// this posts each direction's *total* as its own pair rather than one pair per line:
///
/// - IncreaseAmount &gt; 0: Debit InventoryAccountId, Credit AdjustmentAccountId (found/corrected-in
///   stock -- an asset increase funded by a contra-income recognition on the Adjustment account).
/// - DecreaseAmount &gt; 0: Debit AdjustmentAccountId, Credit InventoryAccountId (damage/write-off --
///   an asset decrease expensed to the Adjustment account).
///
/// Net effect on InventoryAccountId = IncreaseAmount - DecreaseAmount (Debit minus Credit), exactly
/// the real change in on-hand stock value -- correct regardless of how the two amounts compare.
/// Net effect on AdjustmentAccountId = IncreaseAmount - DecreaseAmount too, but as Credit minus
/// Debit: a net credit (found more than decreased) reads as other income, a net debit (wrote off
/// more than found) reads as an expense -- both standard uses of a single Adjustment/Variance
/// control account. Sum(Debit) = IncreaseAmount + DecreaseAmount = Sum(Credit) always, so
/// GlJournalEntry.Post's balanced-invariant holds regardless of which direction dominates.
/// </summary>
public sealed class InventoryAdjustmentPostingRule : IGlPostingRule<InventoryAdjustmentPostingInput>
{
    public IReadOnlyList<GlLineInput> BuildLines(InventoryAdjustmentPostingInput document)
    {
        var lines = new List<GlLineInput>();

        if (document.IncreaseAmount > 0)
        {
            lines.Add(new GlLineInput(document.InventoryAccountId, document.IncreaseAmount, 0));
            lines.Add(new GlLineInput(document.AdjustmentAccountId, 0, document.IncreaseAmount));
        }

        if (document.DecreaseAmount > 0)
        {
            lines.Add(new GlLineInput(document.AdjustmentAccountId, document.DecreaseAmount, 0));
            lines.Add(new GlLineInput(document.InventoryAccountId, 0, document.DecreaseAmount));
        }

        return lines;
    }
}
