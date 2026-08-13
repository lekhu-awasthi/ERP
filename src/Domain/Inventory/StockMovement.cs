using ErpApp.Domain.Common;

namespace ErpApp.Domain.Inventory;

/// <summary>
/// A lightweight, append-only audit trail paired with every IStockLedgerService.IncrementAsync/
/// ConsumeAsync call -- not itself a FIFO layer (StockLedgerEntry owns that), and never read back
/// by the FIFO engine. Exists purely so InventoryLedgerQuery's kardex view (roadmap Phase 7 task 8:
/// "chronological StockLedgerEntry movements... running balance") can show OUT events at all --
/// StockLedgerEntry.QuantityRemaining mutates in place as Consume walks layers, so once a layer has
/// been partially consumed by more than one document, the *history* of which document took how much
/// is gone from StockLedgerEntry alone. One StockMovement row is written per Increment/Consume
/// call (not per FIFO layer touched inside a multi-layer Consume) -- a kardex reports one line per
/// document/transaction, not per internal layer-walk step, and Consume's own weighted-average
/// UnitCost is exactly the right figure to show for an Out row anyway.
/// </summary>
public sealed class StockMovement
{
    public Guid Id { get; private set; }
    public Guid OrganizationId { get; private set; }
    public Guid ProductId { get; private set; }
    public Guid WarehouseId { get; private set; }
    public StockMovementDirection Direction { get; private set; }
    public decimal Quantity { get; private set; }
    public decimal UnitCost { get; private set; }
    public DocumentType SourceDocumentType { get; private set; }
    public Guid SourceDocumentId { get; private set; }
    public DateOnly TransactionDate { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }

    private StockMovement()
    {
    }

    /// <summary>Public (not internal) for the same cross-assembly reason as
    /// StockLedgerEntry.Consume -- IStockLedgerService, the only intended caller, lives in the
    /// Application assembly.</summary>
    public static StockMovement Create(
        Guid organizationId,
        Guid productId,
        Guid warehouseId,
        StockMovementDirection direction,
        decimal quantity,
        decimal unitCost,
        DocumentType sourceDocumentType,
        Guid sourceDocumentId,
        DateOnly transactionDate)
    {
        return new StockMovement
        {
            Id = Guid.NewGuid(),
            OrganizationId = organizationId,
            ProductId = productId,
            WarehouseId = warehouseId,
            Direction = direction,
            Quantity = quantity,
            UnitCost = unitCost,
            SourceDocumentType = sourceDocumentType,
            SourceDocumentId = sourceDocumentId,
            TransactionDate = transactionDate,
            CreatedAt = DateTimeOffset.UtcNow,
        };
    }
}
