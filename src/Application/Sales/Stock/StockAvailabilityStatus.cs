namespace ErpApp.Application.Sales.Stock;

/// <summary>Mirrors architecture-spec.md §3.5's Ok|Warn|Reject shape for the real FIFO-backed
/// policy Phase 7 will build. Nothing produces Warn/Reject yet -- see
/// AlwaysOkStockAvailabilityPolicy's doc comment.</summary>
public enum StockAvailabilityStatus
{
    Ok,
    Warn,
    Reject,
}
