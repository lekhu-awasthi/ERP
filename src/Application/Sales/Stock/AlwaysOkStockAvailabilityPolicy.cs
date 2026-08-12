using ErpApp.Domain.Sales;

namespace ErpApp.Application.Sales.Stock;

/// <summary>
/// Literal always-Ok stub -- there's no StockLedgerEntry (Phase 7) to check availability against
/// yet, so this phase's Invoice approval never blocks or warns on stock. "Build the seam, not the
/// feature", same judgment call as JournalVoucherStatus.Void (see phase-4-status.md's scope
/// decisions) -- ApproveInvoiceCommandHandler already calls through IStockAvailabilityPolicy, so
/// swapping this for a real FIFO-backed implementation in Phase 7 needs no handler changes.
/// </summary>
public sealed class AlwaysOkStockAvailabilityPolicy : IStockAvailabilityPolicy
{
    public StockAvailabilityStatus Check(Invoice invoice) => StockAvailabilityStatus.Ok;
}
