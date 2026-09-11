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

    /// <summary>
    /// Phase 35b -- the <b>billing location</b> of the document that produced this row, stamped at
    /// write time.
    ///
    /// <para>The same decision as <c>GlJournalEntry.LocationId</c>, taken for the same reason and
    /// deliberately not taken twice: an append-only fact table that points back at its source with
    /// (SourceDocumentType, SourceDocumentId) and nothing else cannot be filtered by location
    /// without either a join across every producing type or a column. Six inventory reports carry a
    /// Billing Location filter live, and a stock report aggregates a whole period before it pages,
    /// so the join would run over every movement in the window on every request (phase 34c:
    /// the report layer's cost is the period, not the page).</para>
    ///
    /// <para>Null means the producing document had no location -- raised while its type was outside
    /// the tenant's <c>LocationScopeMode</c>, or older than this phase's backfill. A filter for a
    /// specific location excludes those rows, because a null never equals a value.</para>
    /// </summary>
    public Guid? LocationId { get; private set; }

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
        DateOnly transactionDate,
        Guid? locationId = null)
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
            LocationId = locationId,
            CreatedAt = DateTimeOffset.UtcNow,
        };
    }
}
