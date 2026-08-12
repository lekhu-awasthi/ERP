using ErpApp.Domain.Payments;

namespace ErpApp.Application.Payments.Posting;

/// <summary>Pure input shape for PaymentPostingRule.BuildLines. ControlAccountId is Accounts
/// Receivable when Direction=Received, Accounts Payable when Direction=Paid -- neither is stored
/// on the Payment aggregate, so PaymentAccountResolver resolves the right one from TenantSettings
/// once (I/O) before the rule runs (pure). Same reasoning as Sales.Posting.InvoicePostingInput.</summary>
public sealed record PaymentPostingInput(Guid CashOrBankAccountId, Guid ControlAccountId, decimal Amount, PaymentDirection Direction);
