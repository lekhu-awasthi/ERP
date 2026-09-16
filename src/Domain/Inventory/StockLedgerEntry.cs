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
    /// Phase 51 -- the <b>batch</b> this layer belongs to, or null when the product is not
    /// batch-tracked (which is every layer written before this phase, and every layer of every
    /// product whose Batch Tracking flag is off).
    ///
    /// <para>This is the whole of the batch dimension: <see cref="Domain.Catalog.ProductBatch"/>
    /// stores no quantity, so a batch's on-hand is <c>SUM(QuantityRemaining)</c> over the layers
    /// carrying its id, grouped by warehouse. Keying the layer rather than hanging a second ledger
    /// off it is what makes the phase-25 conservation law hold by construction: a dimension that is
    /// a GROUP BY cannot drift from the thing it groups.</para>
    ///
    /// <para>A <b>shortfall</b> layer carries whatever key the request named -- the batch when a
    /// line named one and that batch came up short, null when the FIFO walk across every batch came
    /// up short, because units that were never received belong to no batch.</para>
    /// </summary>
    public Guid? BatchId { get; private set; }

    /// <summary>
    /// Phase 51 -- the <b>serial number</b> of the single physical unit this layer is, or null when
    /// the product is not serial-tracked.
    ///
    /// <para>When it is set, <see cref="QuantityIn"/> is exactly <c>1</c>, and that is the model:
    /// a serial is a layer of quantity one. Its lifecycle -- the <i>Status</i> filter on the report
    /// catalogue, which the product tab's three columns do not explain -- is
    /// <see cref="QuantityRemaining"/>: <c>1</c> is In Stock and <c>0</c> is Issued. Nothing had to
    /// be invented for it; it was already in the ledger.</para>
    ///
    /// <para>Uniqueness is over <b>in-stock</b> layers only: a serial that is issued and later
    /// returned re-enters stock as a new layer with the same number, while the old row stays as the
    /// history the kardex reconstructs from. See the filtered unique index in
    /// <c>StockLedgerEntryConfiguration</c>.</para>
    /// </summary>
    public string? SerialNo { get; private set; }

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
        DateOnly transactionDate,
        Guid? locationId = null,
        Guid? batchId = null,
        string? serialNo = null)
    {
        if (quantityIn <= 0)
        {
            throw new InvalidOperationException("A stock ledger entry needs a positive Quantity In.");
        }

        if (unitCost < 0)
        {
            throw new InvalidOperationException("A stock ledger entry's Unit Cost cannot be negative.");
        }

        // Phase 51 -- a serial IS the layer, so a serialised layer of any other size would be a
        // second physical unit sharing one number. Every caller splits a serialised line into one
        // Increment per serial before it reaches here; this is the invariant that says so.
        if (serialNo is not null && quantityIn != 1m)
        {
            throw new InvalidOperationException(
                "A serial-numbered stock ledger entry is exactly one physical unit, so its Quantity In must be 1.");
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
            LocationId = locationId,
            BatchId = batchId,
            SerialNo = Normalize(serialNo),
            CreatedAt = DateTimeOffset.UtcNow,
        };
    }

    /// <summary>
    /// Phase 37 -- a <b>shortfall layer</b>: the FIFO record of stock that was issued before it was
    /// ever received, created only when the tenant's <c>NegativeStockBalanceAction</c> is Warn (and
    /// the warning was confirmed) or Do Nothing. <paramref name="shortfallQuantity"/> is a positive
    /// magnitude; the row stores it negated, so <see cref="QuantityIn"/> and
    /// <see cref="QuantityRemaining"/> are both negative and the sum of QuantityRemaining across
    /// layers -- which is what <c>GetAvailableQuantityAsync</c> and every stock figure derive from --
    /// goes negative by exactly the amount owed.
    ///
    /// <para><paramref name="assumedUnitCost"/> is the product's last known cost in that warehouse
    /// (zero when it has never been bought there). It is an <i>assumption</i>, and the difference
    /// between it and what the covering receipt actually cost is the phase's central number: see
    /// <see cref="Fill"/>.</para>
    /// </summary>
    public static StockLedgerEntry CreateShortfall(
        Guid organizationId,
        Guid productId,
        Guid warehouseId,
        decimal shortfallQuantity,
        decimal assumedUnitCost,
        DocumentType sourceDocumentType,
        Guid sourceDocumentId,
        DateOnly transactionDate,
        Guid? locationId = null,
        Guid? batchId = null)
    {
        if (shortfallQuantity <= 0)
        {
            throw new InvalidOperationException("A shortfall layer needs a positive shortfall quantity.");
        }

        if (assumedUnitCost < 0)
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
            QuantityIn = -shortfallQuantity,
            QuantityRemaining = -shortfallQuantity,
            UnitCost = assumedUnitCost,
            TransactionDate = transactionDate,
            LocationId = locationId,
            BatchId = batchId,
            CreatedAt = DateTimeOffset.UtcNow,
        };
    }

    /// <summary>
    /// Phase 37 -- pays a shortfall layer back down towards zero as a real receipt arrives. Only
    /// ever called on a layer created by <see cref="CreateShortfall"/> (QuantityRemaining below
    /// zero), and never past zero: a receipt bigger than the debt fills it exactly and keeps the
    /// rest as its own positive layer.
    ///
    /// <para><see cref="QuantityIn"/> is deliberately left alone, exactly as <see cref="Consume"/>
    /// leaves it -- it is the layer's original size and the kardex's reconstruction depends on it.
    /// </para>
    /// </summary>
    public void Fill(decimal quantity)
    {
        if (quantity <= 0)
        {
            throw new InvalidOperationException("Filled quantity must be positive.");
        }

        if (QuantityRemaining >= 0)
        {
            throw new InvalidOperationException("Only a shortfall layer can be filled.");
        }

        if (quantity > -QuantityRemaining)
        {
            throw new InvalidOperationException("Cannot fill a shortfall layer past zero.");
        }

        QuantityRemaining += quantity;
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

    private static string? Normalize(string? value)
    {
        var trimmed = value?.Trim();
        return string.IsNullOrEmpty(trimmed) ? null : trimmed;
    }
}
