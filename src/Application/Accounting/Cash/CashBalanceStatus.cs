namespace ErpApp.Application.Accounting.Cash;

/// <summary>
/// The third use of the Ok|Warn|Reject shape (after <c>StockAvailabilityStatus</c> and
/// <c>CreditLimitStatus</c>), for <c>TenantSettings.NegativeCashBalanceAction</c>. Ok = no
/// Bank/Cash account is driven below zero, or the setting is DoNothing. Warn = it is, and the
/// setting is Warn -- the approving handler throws a confirmable <c>CashBalanceWarningException</c>
/// unless the command's override flag is set. Reject = a hard ConflictException.
/// </summary>
public enum CashBalanceStatus
{
    Ok,
    Warn,
    Reject,
}
