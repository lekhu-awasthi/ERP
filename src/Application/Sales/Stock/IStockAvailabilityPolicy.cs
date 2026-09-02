using ErpApp.Domain.Sales;

namespace ErpApp.Application.Sales.Stock;

/// <summary>
/// The seam ApproveInvoiceCommandHandler calls on the decrement side (architecture-spec.md §3.5).
/// Phase 5 shipped this as a literal always-Ok stub (AlwaysOkStockAvailabilityPolicy, now removed);
/// Phase 7's FifoStockAvailabilityPolicy is the real implementation, comparing each Goods line's
/// requested Quantity against IStockLedgerService.GetAvailableQuantityAsync for
/// (ProductId, Invoice.WarehouseId) and branching on TenantSettings.NegativeStockBalanceAction.
/// Async because the real implementation needs DB reads the Phase 5 stub never did.
///
/// <para>Phase 25 (docs/phase-25-status.md Decision F) added
/// <see cref="CheckRequirementsAsync"/> and made <see cref="CheckAsync"/> a thin adapter over it,
/// so a Production Journal's raw materials go through the tenant's <i>real</i>
/// NegativeStockBalanceAction rather than a hardcoded throw. The alternatives were generalising
/// this interface over an IStockConsumingDocument abstraction (a new abstraction for two callers)
/// or a parallel policy (two homes for one rule -- the way a Reject tenant ends up rejecting
/// invoices but warning on production). One extra method keeps a single place where that setting
/// is consulted. The namespace stays <c>Sales.Stock</c> deliberately: moving it to Inventory.Stock
/// where it now arguably belongs would touch about twenty files for a rename, so Manufacturing
/// takes the one odd-looking using directive instead.</para>
/// </summary>
public interface IStockAvailabilityPolicy
{
    Task<StockAvailabilityStatus> CheckAsync(Invoice invoice, CancellationToken cancellationToken);

    /// <summary>
    /// Document-agnostic form: does this warehouse hold enough of each product, and if not, what
    /// does the tenant want done about it? Quantities for the same product are expected to be
    /// pre-summed by the caller (a document may name one product on several lines).
    /// </summary>
    Task<StockAvailabilityStatus> CheckRequirementsAsync(
        Guid organizationId,
        Guid warehouseId,
        IReadOnlyCollection<StockRequirement> requirements,
        CancellationToken cancellationToken);
}

/// <summary>One product's total requirement against a single warehouse.</summary>
public sealed record StockRequirement(Guid ProductId, decimal Quantity);
