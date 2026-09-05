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

public sealed record PurchaseBillPostingLineInput(Guid DebitAccountId, decimal Amount, decimal VatAmount);
