namespace ErpApp.Application.Purchasing.Posting;

/// <summary>
/// Pure input shape for PurchaseBillPostingRule.BuildLines -- same resolved-input-record split
/// Sales.Posting.InvoicePostingInput uses: a PurchaseBill's GL lines need each line's resolved
/// debit account (Inventory for a Goods line, Purchase Expense for a Service line -- see
/// PurchaseBillAccountResolver) plus the tenant's Accounts Payable/VAT Receivable/TDS Payable
/// accounts, none of which live on the PurchaseBill aggregate itself -- PurchaseBillAccountResolver
/// resolves them once (I/O) before the rule runs (pure).
/// </summary>
public sealed record PurchaseBillPostingInput(
    Guid AccountsPayableAccountId,
    Guid VatReceivableAccountId,
    Guid TdsPayableAccountId,
    decimal TdsAmount,
    IReadOnlyList<PurchaseBillPostingLineInput> Lines);

public sealed record PurchaseBillPostingLineInput(Guid DebitAccountId, decimal Amount, decimal VatAmount);
