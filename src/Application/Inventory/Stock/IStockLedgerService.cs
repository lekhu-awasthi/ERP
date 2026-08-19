using ErpApp.Domain.Common;

namespace ErpApp.Application.Inventory.Stock;

/// <summary>
/// The real FIFO consumption engine (architecture-spec.md §3.5, roadmap Phase 7 task 2) --
/// a plain injectable service, not a MediatR handler, mirroring how IGlPostingRule/
/// IDocumentNumberGenerator are structured. Called from ApproveInvoiceCommandHandler/
/// ApprovePurchaseBillCommandHandler/ApproveWarehouseTransferCommandHandler/
/// ApproveInventoryAdjustmentCommandHandler -- never from a Create handler, same "side effects
/// happen at Approve, not Create" rule every other document type in this codebase follows.
/// </summary>
public interface IStockLedgerService
{
    /// <summary>Creates one new StockLedgerEntry layer. A zero Quantity is a no-op (no layer
    /// created) -- defensive only, since every real caller already validates a positive line
    /// quantity before reaching here.</summary>
    Task IncrementAsync(
        Guid organizationId,
        Guid productId,
        Guid warehouseId,
        decimal quantity,
        decimal unitCost,
        DocumentType sourceDocumentType,
        Guid sourceDocumentId,
        DateOnly transactionDate,
        CancellationToken cancellationToken);

    /// <summary>
    /// Walks existing layers for (ProductId, WarehouseId) oldest-TransactionDate-first (ties
    /// broken by CreatedAt), decrementing QuantityRemaining across as many layers as needed.
    /// Returns the weighted-average UnitCost of what was actually consumed -- the COGS figure a
    /// caller multiplies back by Quantity to get the line's total cost of goods sold. A zero
    /// Quantity is a no-op, returning 0. Throws <see cref="Common.Exceptions.ConflictException"/>
    /// (not a raw 500) if Quantity exceeds the total remaining across every layer -- this engine
    /// never lets a layer, or the product/warehouse total, go negative; callers that want a
    /// pre-flight check use <see cref="GetAvailableQuantityAsync"/> first (see
    /// IStockAvailabilityPolicy).
    /// </summary>
    Task<decimal> ConsumeAsync(
        Guid organizationId,
        Guid productId,
        Guid warehouseId,
        decimal quantity,
        DocumentType sourceDocumentType,
        Guid sourceDocumentId,
        DateOnly transactionDate,
        CancellationToken cancellationToken);

    /// <summary>Sum of QuantityRemaining across every layer for (ProductId, WarehouseId) -- the
    /// on-hand balance IStockAvailabilityPolicy compares a requested quantity against.</summary>
    Task<decimal> GetAvailableQuantityAsync(
        Guid organizationId, Guid productId, Guid warehouseId, CancellationToken cancellationToken);

    /// <summary>
    /// Read-only, non-mutating estimate of what ConsumeAsync would return if called right now --
    /// used by GL-preview-before-approve (architecture-spec.md §3.4's live-preview requirement,
    /// extended to Invoice's COGS leg in Phase 7). If the requested Quantity exceeds what's
    /// actually available, this does NOT throw the way ConsumeAsync does -- it silently caps at
    /// whatever layers exist and returns their weighted-average cost (0 if none exist), since a
    /// preview must never block just because the real Approve hasn't happened yet. Callers should
    /// treat the result as an estimate, not a guarantee -- see phase-7-status.md's scope decision.
    /// </summary>
    Task<decimal> PreviewConsumptionCostAsync(
        Guid organizationId, Guid productId, Guid warehouseId, decimal quantity, CancellationToken cancellationToken);

    /// <summary>
    /// Void lifecycle (roadmap Phase 16a): undoes every layer <see cref="IncrementAsync"/> created
    /// for (sourceDocumentType, sourceDocumentId) -- a voided PurchaseBill's own layers, a voided
    /// WarehouseTransfer's destination-side layer, a voided InventoryAdjustment Increase line's
    /// layer, or a voided CreditNote's restock layer. Throws
    /// <see cref="Common.Exceptions.ConflictException"/> (409, not a partial unwind) if ANY of
    /// those layers has already been partly or fully consumed by a *later* document (QuantityRemaining
    /// less than QuantityIn) -- the roadmap's explicit requirement that a partly-consumed layer's
    /// source document must be rejected, not silently unwound underneath whatever consumed it.
    /// QuantityIn is left untouched (kardex history stays reconstructable, same as Consume's own
    /// invariant); only QuantityRemaining drops to zero, with a StockMovement Out row recorded for
    /// audit. A no-op (no layers found) is not an error -- a voided document whose lines were all
    /// Service products, or a standalone reversal that never touched stock, has nothing to undo.
    /// </summary>
    Task ReverseIncrementAsync(
        Guid organizationId,
        DocumentType sourceDocumentType,
        Guid sourceDocumentId,
        DateOnly transactionDate,
        CancellationToken cancellationToken);
}
