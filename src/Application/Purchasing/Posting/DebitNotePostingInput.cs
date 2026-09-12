namespace ErpApp.Application.Purchasing.Posting;

/// <summary>
/// Same shape as PurchaseBillPostingInput, including the TDS fields -- a DebitNote is a full
/// mirror of PurchaseBillPostingRule now, including its TDS leg (see DebitNote's doc comment for
/// why the earlier TDS-free version left Accounts Payable unbalanced after a full reversal). Kept
/// as a distinct type (not a reused generic registration) so DI can register a separate
/// IGlPostingRule&lt;T&gt; implementation for the reversed DebitNote posting.
///
/// <para>Phase 29 (FR-6.15) adds the landed-cost mirror: <see cref="ReleasedAdditionalCost"/> is the
/// share of the source bill's capitalised Additional Cost that the returned quantities carried, and
/// it is zero for every debit note whose source bill had no Additional Cost section.</para>
///
/// <para><b>Phase 37 closes the modelling gap phase 29 stopped widening.</b>
/// <see cref="RelievedInventoryCost"/> is what the FIFO layers actually gave up -- the sum of
/// quantity times the weighted-average cost <c>ConsumeAsync</c> returned, landed cost included --
/// and when it is present the rule credits Inventory that, instead of crediting each Goods line its
/// return price and then correcting for freight. It is null for a standalone debit note (one with
/// no source Purchase Bill), which touches no stock at all and posts exactly what it always did.
/// <see cref="InventoryAdjustmentAccountId"/> takes whatever difference is left between the price
/// the supplier credits and the cost the goods carried -- see the rule.</para>
/// </summary>
public sealed record DebitNotePostingInput(
    Guid AccountsPayableAccountId,
    Guid VatReceivableAccountId,
    Guid TdsPayableAccountId,
    decimal TdsAmount,
    IReadOnlyList<PurchaseBillPostingLineInput> Lines,
    Guid? InventoryAccountId = null,
    Guid? LandedCostClearingAccountId = null,
    decimal ReleasedAdditionalCost = 0,
    decimal? RelievedInventoryCost = null,
    Guid? InventoryAdjustmentAccountId = null);
