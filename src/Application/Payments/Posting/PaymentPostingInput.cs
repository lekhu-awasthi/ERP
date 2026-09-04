using ErpApp.Domain.Payments;

namespace ErpApp.Application.Payments.Posting;

/// <summary>Pure input shape for PaymentPostingRule.BuildLines. ControlAccountId is Accounts
/// Receivable when Direction=Received, Accounts Payable when Direction=Paid -- neither is stored
/// on the Payment aggregate, so PaymentAccountResolver resolves the right one from TenantSettings
/// once (I/O) before the rule runs (pure). Same reasoning as Sales.Posting.InvoicePostingInput.</summary>
public sealed record PaymentPostingInput(
    Guid CashOrBankAccountId,
    Guid ControlAccountId,
    decimal Amount,
    PaymentDirection Direction,
    PaymentForexInput? Forex = null);

/// <summary>
/// Phase 28 (FR-2.5) -- the realised exchange difference this payment's allocations produce,
/// already netted, already resolved to an account and already expressed in the base currency.
/// Null whenever there is no difference at all, which is every payment a single-currency tenant
/// will ever make and most of a multi-currency one's, so the rule's default shape is untouched.
///
/// <para><see cref="Amount"/> is always positive; <see cref="IsGain"/> carries the direction,
/// because which side of the control account the correction lands on depends on both the sign of
/// the difference and the payment's own <see cref="PaymentDirection"/> -- see
/// PaymentForexCalculator, which is where that reasoning lives and is tested.</para>
/// </summary>
public sealed record PaymentForexInput(Guid ForexAccountId, decimal Amount, bool IsGain);
