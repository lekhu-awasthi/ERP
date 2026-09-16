using ErpApp.Domain.Common;

namespace ErpApp.Application.Inventory.Stock;

/// <summary>
/// The real FIFO consumption engine (architecture-spec.md §3.5, roadmap Phase 7 task 2) --
/// a plain injectable service, not a MediatR handler, mirroring how IGlPostingRule/
/// IDocumentNumberGenerator are structured. Called from ApproveInvoiceCommandHandler/
/// ApprovePurchaseBillCommandHandler/ApproveWarehouseTransferCommandHandler/
/// ApproveInventoryAdjustmentCommandHandler -- never from a Create handler, same "side effects
/// happen at Approve, not Create" rule every other document type in this codebase follows.
///
/// <para><b>Phase 51 -- the batch and serial dimensions.</b> Both are keys on the FIFO layer, not
/// ledgers of their own, so they reach this interface as two more arguments and change nothing
/// about the walk. A <c>batchId</c> narrows which layers may be chosen; a <c>serialNo</c> names
/// exactly one. Both are null for every product whose flags are off, which is every call on a
/// tenant that never turns them on.</para>
/// </summary>
public interface IStockLedgerService
{
    /// <summary>Creates one new StockLedgerEntry layer. A zero Quantity is a no-op (no layer
    /// created) -- defensive only, since every real caller already validates a positive line
    /// quantity before reaching here.
    ///
    /// <para><b>Phase 37 -- returns the cost catch-up.</b> If this (product, warehouse) owes stock
    /// -- a shortfall layer left behind by an oversell -- the receipt pays that debt down before it
    /// becomes stock on hand, and the returned amount is how much less the remaining stock is worth
    /// than this receipt's own value implies: <c>filled x (unitCost - the cost the shortfall was
    /// issued at)</c>. It is zero in every other case, which is every call on a tenant that has
    /// never oversold. <b>A caller must post a non-zero result</b> -- <c>StockCostCatchUp.PostAsync</c>
    /// is the one way to do that -- or the general ledger's Inventory balance drifts from the FIFO
    /// layers by exactly this figure.</para>
    ///
    /// <para><b>Phase 51.</b> <paramref name="serialNo"/> makes this layer one physical unit, so
    /// <paramref name="quantity"/> must then be exactly 1 -- a caller receiving five serialised
    /// units calls this five times. A shortfall is paid down by a receipt of the <i>same</i> batch
    /// first and by an un-batched one second; see the remarks on the implementation for why the
    /// second half is load-bearing.</para></summary>
    Task<decimal> IncrementAsync(
        Guid organizationId,
        Guid productId,
        Guid warehouseId,
        decimal quantity,
        decimal unitCost,
        DocumentType sourceDocumentType,
        Guid sourceDocumentId,
        DateOnly transactionDate,
        CancellationToken cancellationToken,
        Guid? locationId = null,
        Guid? batchId = null,
        string? serialNo = null);

    /// <summary>
    /// Walks existing layers for (ProductId, WarehouseId) oldest-TransactionDate-first (ties
    /// broken by CreatedAt), decrementing QuantityRemaining across as many layers as needed.
    /// Returns a <see cref="StockConsumption"/>: the weighted-average UnitCost of what was actually
    /// consumed -- the COGS figure a caller multiplies back by Quantity to get the line's total cost
    /// of goods sold -- together with the per-layer reliefs that make it up. A zero Quantity is a
    /// no-op, returning <see cref="StockConsumption.None"/>. Throws
    /// <see cref="Common.Exceptions.ConflictException"/> (not a raw 500) if Quantity exceeds the
    /// total remaining across every eligible layer and <paramref name="allowNegative"/> is false;
    /// callers that want a pre-flight check use <see cref="GetAvailableQuantityAsync"/> first
    /// (see IStockAvailabilityPolicy).
    ///
    /// <para><b>Phase 37 -- <paramref name="allowNegative"/> is the tenant's Negative Item Balance
    /// setting, already decided.</b> This method does not read that setting: it is handed the
    /// verdict <see cref="Sales.Stock.IStockAvailabilityPolicy"/> reached, because a Warn verdict
    /// also has to have been confirmed by the user before the shortfall is allowed, and only the
    /// command handler knows that. When true, the part of the request the layers cannot cover
    /// becomes a <b>shortfall layer</b> (<see cref="Domain.Inventory.StockLedgerEntry.CreateShortfall"/>)
    /// at the product's last known cost in that warehouse, and the returned weighted average blends
    /// that assumed cost in -- so the caller's COGS leg still equals what the ledger lost.</para>
    ///
    /// <para><b>Phase 51.</b> <paramref name="batchId"/> narrows the walk to one batch and a
    /// shortfall then carries that batch; leaving it null walks every batch oldest-first and a
    /// shortfall then carries none. <paramref name="serialNo"/> names one physical unit, and a
    /// serialised consume <b>never</b> goes negative whatever <paramref name="allowNegative"/> says
    /// -- "issue a serial that was never received" is a typo, not an oversell.</para>
    /// </summary>
    Task<StockConsumption> ConsumeAsync(
        Guid organizationId,
        Guid productId,
        Guid warehouseId,
        decimal quantity,
        DocumentType sourceDocumentType,
        Guid sourceDocumentId,
        DateOnly transactionDate,
        CancellationToken cancellationToken,
        Guid? locationId = null,
        bool allowNegative = false,
        Guid? batchId = null,
        string? serialNo = null);

    /// <summary>Sum of QuantityRemaining across every layer for (ProductId, WarehouseId) -- the
    /// on-hand balance IStockAvailabilityPolicy compares a requested quantity against. Phase 51:
    /// <paramref name="batchId"/> narrows it to one batch, which is what an availability check for
    /// a line that named a batch has to compare against.</summary>
    Task<decimal> GetAvailableQuantityAsync(
        Guid organizationId,
        Guid productId,
        Guid warehouseId,
        CancellationToken cancellationToken,
        Guid? batchId = null);

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
        Guid organizationId,
        Guid productId,
        Guid warehouseId,
        decimal quantity,
        CancellationToken cancellationToken,
        Guid? batchId = null,
        string? serialNo = null);

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
    ///
    /// <para><b>Phase 51.</b> The reversal takes the layer's own BatchId and SerialNo, exactly as it
    /// already takes its own WarehouseId and LocationId and for the same reason (phase 35b): a
    /// release that landed in a different batch would leave that batch's derived quantity
    /// permanently wrong while the product-wide total still reconciled. That is phase 43's failure
    /// precisely, and the reason it was found in production shape rather than in a test.</para>
    /// </summary>
    Task ReverseIncrementAsync(
        Guid organizationId,
        DocumentType sourceDocumentType,
        Guid sourceDocumentId,
        DateOnly transactionDate,
        CancellationToken cancellationToken);
}
