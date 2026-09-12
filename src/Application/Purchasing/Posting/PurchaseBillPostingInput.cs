namespace ErpApp.Application.Purchasing.Posting;

/// <summary>
/// Pure input shape for PurchaseBillPostingRule.BuildLines -- same resolved-input-record split
/// Sales.Posting.InvoicePostingInput uses: a PurchaseBill's GL lines need each line's resolved
/// debit account (Inventory for a Goods line, Purchase Expense for a Service line -- see
/// PurchaseBillAccountResolver) plus the tenant's Accounts Payable/VAT Receivable/TDS Payable
/// accounts, none of which live on the PurchaseBill aggregate itself -- PurchaseBillAccountResolver
/// resolves them once (I/O) before the rule runs (pure).
///
/// <para>Phase 29 adds the landed-cost pair. <see cref="CapitalisedAdditionalCost"/> is filled in by
/// ApprovePurchaseBillCommandHandler <i>after</i> the FIFO layers exist, because it is defined as
/// the value those layers actually received beyond the goods amounts -- phase-25's rule that a GL
/// entry is built from the values actually created, not from the theoretical figures that produced
/// them. It is zero for every bill with no Additional Cost section, in which case both account ids
/// are null and the rule emits exactly what it always did.</para>
/// </summary>
public sealed record PurchaseBillPostingInput(
    Guid AccountsPayableAccountId,
    Guid VatReceivableAccountId,
    Guid TdsPayableAccountId,
    decimal TdsAmount,
    IReadOnlyList<PurchaseBillPostingLineInput> Lines,
    Guid? InventoryAccountId = null,
    Guid? LandedCostClearingAccountId = null,
    decimal CapitalisedAdditionalCost = 0);

/// <summary>
/// One resolved document line. <paramref name="RelievesStock"/> (phase 37) says this line's product
/// is Goods and its account is therefore the Inventory account -- the flag
/// <c>DebitNotePostingRule</c> needs to leave such a line out of its own credit grouping, because a
/// purchase return credits Inventory what the FIFO layers actually lost rather than what the line
/// says. It is set by <c>PurchaseBillAccountResolver</c>, which is the only place that knows a
/// line's Product.Type, and comparing the resolved account against the Inventory account instead
/// would have been a guess on any tenant that points two defaults at one account.
/// <c>PurchaseBillPostingRule</c> ignores it: a bill debits Inventory the amount it paid, which is
/// the line amount.
/// </summary>
public sealed record PurchaseBillPostingLineInput(
    Guid DebitAccountId, decimal Amount, decimal VatAmount, bool RelievesStock = false);
