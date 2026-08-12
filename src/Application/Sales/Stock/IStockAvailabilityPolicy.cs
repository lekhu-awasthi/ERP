using ErpApp.Domain.Sales;

namespace ErpApp.Application.Sales.Stock;

/// <summary>
/// The seam ApproveInvoiceCommandHandler calls on the decrement side (architecture-spec.md §3.5) --
/// deliberately stubbed this phase (roadmap's own sequencing recommendation (a): ship the Sales
/// chain's accounting/document behavior first, backfill real stock enforcement once Phase 7's FIFO
/// ledger exists). See AlwaysOkStockAvailabilityPolicy.
/// </summary>
public interface IStockAvailabilityPolicy
{
    StockAvailabilityStatus Check(Invoice invoice);
}
