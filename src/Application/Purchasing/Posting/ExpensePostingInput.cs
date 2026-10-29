namespace ErpApp.Application.Purchasing.Posting;

/// <summary>
/// Pure input shape for ExpensePostingRule.BuildLines. Simpler than PurchaseBillPostingInput --
/// an Expense's lines already carry their own GL Account directly (no Product->Account fallback
/// resolution needed, closer to Accounting.JournalVoucherPostingRule's "the lines already are the
/// GL lines" shape), so ExpenseAccountResolver only needs to resolve the tenant's Accounts
/// Payable/VAT Receivable/TDS Payable accounts, not per-line accounts.
/// </summary>
public sealed record ExpensePostingInput(
    Guid AccountsPayableAccountId,
    Guid VatReceivableAccountId,
    Guid TdsPayableAccountId,
    decimal TdsAmount,
    IReadOnlyList<ExpensePostingLineInput> Lines);

/// <param name="DisallowedVat">Phase 99 -- see <see cref="PurchaseBillPostingLineInput"/>: posted to the line's own
/// account, not VAT Receivable.</param>
public sealed record ExpensePostingLineInput(Guid AccountId, decimal Amount, decimal VatAmount, decimal DisallowedVat = 0);
