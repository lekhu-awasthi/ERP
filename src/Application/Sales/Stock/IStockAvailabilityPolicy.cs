using ErpApp.Domain.Sales;

namespace ErpApp.Application.Sales.Stock;

/// <summary>
/// The seam ApproveInvoiceCommandHandler calls on the decrement side (architecture-spec.md §3.5).
/// Phase 5 shipped this as a literal always-Ok stub (AlwaysOkStockAvailabilityPolicy, now removed);
/// Phase 7's FifoStockAvailabilityPolicy is the real implementation, comparing each Goods line's
/// requested Quantity against IStockLedgerService.GetAvailableQuantityAsync for
/// (ProductId, Invoice.WarehouseId) and branching on TenantSettings.NegativeStockBalanceAction.
/// Async because the real implementation needs DB reads the Phase 5 stub never did.
/// </summary>
public interface IStockAvailabilityPolicy
{
    Task<StockAvailabilityStatus> CheckAsync(Invoice invoice, CancellationToken cancellationToken);
}
