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
///
/// <para><b>Phase 51 corrects that last sentence.</b> One row is written per distinct <i>relief</i>
/// -- per (BatchId, SerialNo, UnitCost) the walk actually took -- rather than per call. For an
/// untracked product that is still exactly one row and nothing about the kardex changes. For a
/// batch-tracked product it is one row per batch, which is what a kardex for a batch-tracked
/// product should show; for a serialised one it is one row per unit, which is what "a row per
/// physical unit" means.</para>
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

    /// <summary>
    /// Phase 37 -- a <b>value-only</b> correction, in base currency, carried by a row whose
    /// <see cref="Quantity"/> is zero. Always a non-negative magnitude; <see cref="Direction"/>
    /// says which way it moves, exactly as it does for a quantity-bearing row.
    ///
    /// <para><b>Why the column exists.</b> A shortfall layer is issued at an assumed cost and the
    /// covering receipt almost never costs exactly that, so at fill time the stock on hand is worth
    /// a little more or less than In-minus-Out implies. <c>StockFactReader</c> reconstructs every
    /// dated stock figure from this table alone (phase 26c) -- so a correction the FIFO layers and
    /// the general ledger both make has to appear here too, or a dated report drifts from both by
    /// exactly the catch-up. The alternatives were both worse: writing the correction as a matched
    /// In/Out pair of the filled quantity would have inflated the In and Out <i>quantity</i>
    /// columns of every movement report (a wrong quantity is more visible, and less defensible,
    /// than a wrong value), and folding it into the receipt's own unit cost would have misstated
    /// what the receipt cost.</para>
    ///
    /// <para>Zero on every row written before phase 37 and on every quantity-bearing row since.
    /// </para>
    /// </summary>
    public decimal ValueAdjustment { get; private set; }

    /// <summary>Phase 51 -- the batch this movement relieved or created, stamped at write time.
    /// Phase 35b's rule, for the same reason it gave: an append-only fact row carries only its
    /// source ids, so filtering it by a dimension needs a column. A dated batch report reads this
    /// table, never <see cref="StockLedgerEntry"/>, whose QuantityRemaining is decremented in place
    /// and only answers "as of now" (phase 26c).</summary>
    public Guid? BatchId { get; private set; }

    /// <summary>Phase 51 -- the serial number this movement relieved or created. Same rule and same
    /// reason as <see cref="BatchId"/>.</summary>
    public string? SerialNo { get; private set; }

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
        Guid? locationId = null,
        Guid? batchId = null,
        string? serialNo = null)
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
            BatchId = batchId,
            SerialNo = string.IsNullOrWhiteSpace(serialNo) ? null : serialNo.Trim(),
            CreatedAt = DateTimeOffset.UtcNow,
        };
    }

    /// <summary>
    /// Phase 37 -- the value-only row described on <see cref="ValueAdjustment"/>.
    /// <paramref name="signedAmount"/> is the amount by which stock on hand is worth
    /// <i>less</i> than In-minus-Out says (positive when the covering receipt cost more than the
    /// shortfall was issued at), so a positive amount becomes an Out row and a negative one an In
    /// row -- the same sign convention every other row on this table follows. Never call it with
    /// zero: a row that moves nothing is noise on a kardex, and <c>decimal</c> keeps the sign bit
    /// of a negative zero (phase-26c bug #1).
    /// </summary>
    public static StockMovement CreateCostAdjustment(
        Guid organizationId,
        Guid productId,
        Guid warehouseId,
        decimal signedAmount,
        DocumentType sourceDocumentType,
        Guid sourceDocumentId,
        DateOnly transactionDate,
        Guid? locationId = null,
        Guid? batchId = null)
    {
        if (signedAmount == 0)
        {
            throw new InvalidOperationException("A cost adjustment movement needs a non-zero amount.");
        }

        return new StockMovement
        {
            Id = Guid.NewGuid(),
            OrganizationId = organizationId,
            ProductId = productId,
            WarehouseId = warehouseId,
            Direction = signedAmount > 0 ? StockMovementDirection.Out : StockMovementDirection.In,
            Quantity = 0m,
            UnitCost = 0m,
            ValueAdjustment = Math.Abs(signedAmount),
            SourceDocumentType = sourceDocumentType,
            SourceDocumentId = sourceDocumentId,
            TransactionDate = transactionDate,
            LocationId = locationId,
            BatchId = batchId,
            CreatedAt = DateTimeOffset.UtcNow,
        };
    }
}
