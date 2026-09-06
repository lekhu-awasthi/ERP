namespace ErpApp.Application.Sales.Credit;

/// <summary>
/// The Ok|Warn|Reject shape <c>StockAvailabilityStatus</c> already uses, for the third member of
/// the Reject/Warn/DoNothing family. Ok = the contact has no limit set (0 means unlimited), or the
/// projected balance stays inside it, or the tenant's CreditLimitExceedsAction is DoNothing.
/// Warn = the limit is crossed and the setting is Warn -- ApproveInvoiceCommandHandler throws a
/// confirmable <c>CreditLimitWarningException</c> unless the command's OverrideCreditLimitWarning
/// flag is set. Reject = crossed with the setting on Reject, always a hard ConflictException.
/// </summary>
public enum CreditLimitStatus
{
    Ok,
    Warn,
    Reject,
}
