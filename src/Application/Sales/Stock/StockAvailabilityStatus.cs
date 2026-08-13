namespace ErpApp.Application.Sales.Stock;

/// <summary>Mirrors architecture-spec.md §3.5's Ok|Warn|Reject shape. Ok = enough stock (or an
/// all-Service invoice). Warn = a shortfall exists and TenantSettings.NegativeStockBalanceAction
/// is Warn -- ApproveInvoiceCommandHandler throws a confirmable StockAvailabilityWarningException
/// unless the command's OverrideWarning flag is set. Reject = a shortfall exists and the setting is
/// Reject -- always a hard ConflictException, no override possible. See
/// Application.Sales.Stock.FifoStockAvailabilityPolicy.</summary>
public enum StockAvailabilityStatus
{
    Ok,
    Warn,
    Reject,
}
