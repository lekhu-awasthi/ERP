using ErpApp.Domain.Common;

namespace ErpApp.Domain.Inventory;

/// <summary>
/// A single FIFO cost layer (architecture-spec.md §3.5), scoped by (ProductId, WarehouseId).
/// Created only by IStockLedgerService.Increment (Application.Inventory.Stock) -- one row per
/// stock-in event (PurchaseBill line, WarehouseTransfer destination, InventoryAdjustment increase
/// line). QuantityRemaining starts equal to QuantityIn and is decremented in place as later
/// Consume calls walk layers oldest-TransactionDate-first; QuantityIn itself never changes after
/// creation (it's the layer's original size, used to reconstruct history/kardex views).
///
/// TransactionDate is the source document's own business Date (DateOnly), not the row's real-time
/// creation instant -- FIFO ordering must follow the document's effective date so a same-day
/// approval-order quirk (e.g. approving an older-dated PurchaseBill after a newer-dated one) still
/// consumes oldest-dated stock first. CreatedAt is the real insert timestamp, used only as a
/// deterministic tie-breaker when two layers share the same TransactionDate.
/// </summary>
public sealed class StockLedgerEntry
{
    public Guid Id { get; private set; }
    public Guid OrganizationId { get; private set; }
    public Guid ProductId { get; private set; }
    public Guid WarehouseId { get; private set; }
    public DocumentType SourceDocumentType { get; private set; }
    public Guid SourceDocumentId { get; private set; }
    public decimal QuantityIn { get; private set; }
    public decimal QuantityRemaining { get; private set; }
    public decimal UnitCost { get; private set; }
    public DateOnly TransactionDate { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }

    private StockLedgerEntry()
    {
    }

    public static StockLedgerEntry Create(
        Guid organizationId,
        Guid productId,
        Guid warehouseId,
        decimal quantityIn,
        decimal unitCost,
        DocumentType sourceDocumentType,
        Guid sourceDocumentId,
        DateOnly transactionDate)
    {
        if (quantityIn <= 0)
        {
            throw new InvalidOperationException("A stock ledger entry needs a positive Quantity In.");
        }

        if (unitCost < 0)
        {
            throw new InvalidOperationException("A stock ledger entry's Unit Cost cannot be negative.");
        }

        return new StockLedgerEntry
        {
            Id = Guid.NewGuid(),
            OrganizationId = organizationId,
            ProductId = productId,
            WarehouseId = warehouseId,
            SourceDocumentType = sourceDocumentType,
            SourceDocumentId = sourceDocumentId,
            QuantityIn = quantityIn,
            QuantityRemaining = quantityIn,
            UnitCost = unitCost,
            TransactionDate = transactionDate,
            CreatedAt = DateTimeOffset.UtcNow,
        };
    }

    /// <summary>Public (not internal) because IStockLedgerService -- the only intended caller --
    /// lives in the Application assembly, not Domain; "internal" doesn't cross that boundary the
    /// way it does for e.g. InvoiceLine.Create (called from Invoice, same assembly). Only
    /// IStockLedgerService calls this in practice -- it owns the "walk layers oldest-first, don't
    /// consume more than exists across all layers" invariant; this method only guards the
    /// single-layer invariant (can't consume more than this one layer still has).</summary>
    public void Consume(decimal quantity)
    {
        if (quantity <= 0)
        {
            throw new InvalidOperationException("Consumed quantity must be positive.");
        }

        if (quantity > QuantityRemaining)
        {
            throw new InvalidOperationException("Cannot consume more than a stock ledger entry's remaining quantity.");
        }

        QuantityRemaining -= quantity;
    }
}
