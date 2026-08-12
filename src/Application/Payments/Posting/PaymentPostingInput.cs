namespace ErpApp.Application.Payments.Posting;

/// <summary>Pure input shape for PaymentPostingRule.BuildLines -- same reasoning as
/// Sales.Posting.InvoicePostingInput: Accounts Receivable comes from TenantSettings, which isn't
/// stored on the Payment aggregate, so PaymentAccountResolver resolves it once (I/O) before the
/// rule runs (pure).</summary>
public sealed record PaymentPostingInput(Guid CashOrBankAccountId, Guid AccountsReceivableAccountId, decimal Amount);
