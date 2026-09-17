using ErpApp.Application.Common.Exceptions;
using ErpApp.Application.Common.Persistence;
using ErpApp.Domain.Common;
using ErpApp.Domain.Inventory;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.Inventory.Stock;

/// <summary>See IStockLedgerService's doc comment. Adds/mutates entities on the shared IAppDbContext
/// but never calls SaveChangesAsync itself -- same "caller owns the transaction boundary" contract
/// GlJournalEntry.Post's caller follows (roadmap Phase 7 task 4's "both must happen inside the same
/// SaveChangesAsync as the GL posting" requirement flows from this).</summary>
public sealed class StockLedgerService(IAppDbContext db) : IStockLedgerService
{
    public async Task<decimal> IncrementAsync(
        Guid organizationId,
        Guid productId,
        Guid warehouseId,
        PrimaryQuantity primaryQuantity,
        decimal unitCost,
        DocumentType sourceDocumentType,
        Guid sourceDocumentId,
        DateOnly transactionDate,
        CancellationToken cancellationToken,
        Guid? locationId = null,
        Guid? batchId = null,
        string? serialNo = null)
    {
        var quantity = primaryQuantity.Value;

        if (quantity < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(quantity), "Quantity cannot be negative.");
        }

        if (quantity == 0)
        {
            return 0m;
        }

        var entry = StockLedgerEntry.Create(
            organizationId, productId, warehouseId, quantity, unitCost, sourceDocumentType, sourceDocumentId,
            transactionDate, locationId, batchId, serialNo);
        db.StockLedgerEntries.Add(entry);

        db.StockMovements.Add(StockMovement.Create(
            organizationId, productId, warehouseId, StockMovementDirection.In, quantity, unitCost,
            sourceDocumentType, sourceDocumentId, transactionDate, locationId, batchId, serialNo));

        // Phase 37 -- a receipt pays off what is owed before it becomes stock on hand. The new
        // layer is created at its full size first (above) and then consumed down by the fill, which
        // is what keeps ReverseIncrementAsync's "QuantityRemaining != QuantityIn means someone has
        // already taken this" guard true: a receipt that covered a shortfall cannot be voided, and
        // should not be.
        var catchUp = await FillShortfallsAsync(
            organizationId, productId, warehouseId, entry, unitCost, sourceDocumentType, sourceDocumentId,
            transactionDate, locationId, batchId, cancellationToken);

        return catchUp;
    }

    public async Task<StockConsumption> ConsumeAsync(
        Guid organizationId,
        Guid productId,
        Guid warehouseId,
        PrimaryQuantity primaryQuantity,
        DocumentType sourceDocumentType,
        Guid sourceDocumentId,
        DateOnly transactionDate,
        CancellationToken cancellationToken,
        Guid? locationId = null,
        bool allowNegative = false,
        Guid? batchId = null,
        string? serialNo = null)
    {
        var quantity = primaryQuantity.Value;

        if (quantity < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(quantity), "Quantity cannot be negative.");
        }

        if (quantity == 0)
        {
            return StockConsumption.None;
        }

        var layers = await LoadLayersOldestFirstAsync(
            organizationId, productId, warehouseId, batchId, serialNo, cancellationToken);

        var totalAvailable = layers.Sum(x => x.QuantityRemaining);

        // Phase 51 -- a serialised consume is never allowed to go negative, whatever the tenant's
        // Negative Item Balance setting says. That setting exists so a business can sell goods it
        // has not booked in yet; naming a physical unit that has never been received is not that,
        // it is a typo, and a shortfall layer of quantity one carrying a serial number would be a
        // second in-stock row for a serial that does not exist. Same reasoning
        // ApproveWarehouseTransferCommandHandler already applies to its own source side.
        if (serialNo is not null && quantity > totalAvailable)
        {
            throw new ConflictException(
                $"Serial number '{serialNo}' is not in stock in this warehouse, so it cannot be issued.");
        }

        if (quantity > totalAvailable && !allowNegative)
        {
            throw new ConflictException(
                batchId is null
                    ? $"Cannot consume {quantity} unit(s) of this product from this warehouse -- only {totalAvailable} remain in stock."
                    : $"Cannot consume {quantity} unit(s) of this batch from this warehouse -- only {totalAvailable} remain in stock.");
        }

        var remainingToConsume = quantity;
        var totalCost = 0m;
        var reliefs = new List<StockRelief>();

        foreach (var layer in layers)
        {
            if (remainingToConsume <= 0)
            {
                break;
            }

            var consumeFromLayer = Math.Min(remainingToConsume, layer.QuantityRemaining);
            layer.Consume(consumeFromLayer);
            totalCost += consumeFromLayer * layer.UnitCost;
            remainingToConsume -= consumeFromLayer;

            reliefs.Add(new StockRelief(consumeFromLayer, layer.UnitCost, layer.BatchId, layer.SerialNo));
        }

        // Phase 37 -- whatever the layers could not cover becomes a shortfall layer at the
        // product's last known cost in this warehouse. The cost is an assumption and is corrected
        // at fill time (see FillShortfallsAsync); what matters here is that the ledger's value
        // moves by exactly what this method reports back, so the caller's GL leg and the ledger
        // agree the moment they are both written.
        //
        // Phase 51 -- the shortfall carries whatever key the request named. A line that named a
        // batch and found it short owes *that batch*; a walk across every batch that came up short
        // owes no batch at all, because units that were never received belong to none.
        if (remainingToConsume > 0)
        {
            var assumedUnitCost = await LastKnownUnitCostAsync(
                organizationId, productId, warehouseId, batchId, cancellationToken);

            db.StockLedgerEntries.Add(StockLedgerEntry.CreateShortfall(
                organizationId, productId, warehouseId, remainingToConsume, assumedUnitCost,
                sourceDocumentType, sourceDocumentId, transactionDate, locationId, batchId));

            totalCost += remainingToConsume * assumedUnitCost;
            reliefs.Add(new StockRelief(remainingToConsume, assumedUnitCost, batchId, null, Shortfall: true));
        }

        var averageUnitCost = totalCost / quantity;

        // Phase 51 -- one movement row per distinct (batch, serial), not per call. For an untracked
        // product every relief carries (null, null), so this is exactly one row at exactly the
        // weighted average it has always written, and nothing about the kardex changes.
        foreach (var group in reliefs.GroupBy(x => (x.BatchId, x.SerialNo)))
        {
            var groupQuantity = group.Sum(x => x.Quantity);
            var groupCost = group.Sum(x => x.Quantity * x.UnitCost);

            db.StockMovements.Add(StockMovement.Create(
                organizationId, productId, warehouseId, StockMovementDirection.Out, groupQuantity,
                groupCost / groupQuantity, sourceDocumentType, sourceDocumentId, transactionDate, locationId,
                group.Key.BatchId, group.Key.SerialNo));
        }

        return new StockConsumption(averageUnitCost, reliefs);
    }

    public async Task<decimal> GetAvailableQuantityAsync(
        Guid organizationId,
        Guid productId,
        Guid warehouseId,
        CancellationToken cancellationToken,
        Guid? batchId = null)
    {
        var query = db.StockLedgerEntries
            .Where(x => x.OrganizationId == organizationId && x.ProductId == productId && x.WarehouseId == warehouseId);

        // Composed as a second Where rather than folded into the predicate above: an expression tree
        // does not short-circuit, so `batchId == null || x.BatchId == batchId` would hand EF a
        // comparison it evaluates on every row (CLAUDE.md, phase 33/35a).
        if (batchId is not null)
        {
            query = query.Where(x => x.BatchId == batchId);
        }

        return await query.SumAsync(x => x.QuantityRemaining, cancellationToken);
    }

    public async Task<decimal> PreviewConsumptionCostAsync(
        Guid organizationId,
        Guid productId,
        Guid warehouseId,
        PrimaryQuantity primaryQuantity,
        CancellationToken cancellationToken,
        Guid? batchId = null,
        string? serialNo = null)
    {
        var quantity = primaryQuantity.Value;

        if (quantity <= 0)
        {
            return 0m;
        }

        var layers = await LoadLayersOldestFirstAsync(
            organizationId, productId, warehouseId, batchId, serialNo, cancellationToken);

        var remaining = quantity;
        var totalCost = 0m;
        var totalConsidered = 0m;

        foreach (var layer in layers)
        {
            if (remaining <= 0)
            {
                break;
            }

            var take = Math.Min(remaining, layer.QuantityRemaining);
            totalCost += take * layer.UnitCost;
            totalConsidered += take;
            remaining -= take;
        }

        return totalConsidered == 0 ? 0m : totalCost / totalConsidered;
    }

    public async Task ReverseIncrementAsync(
        Guid organizationId,
        DocumentType sourceDocumentType,
        Guid sourceDocumentId,
        DateOnly transactionDate,
        CancellationToken cancellationToken)
    {
        // Phase 37 -- QuantityIn > 0 so this only ever sees layers IncrementAsync created. A single
        // document can now own rows of both kinds: a Production Journal consumes raw materials and
        // receives its output against the same (SourceDocumentType, SourceDocumentId), so if a raw
        // material ran short the shortfall layer sits here too. Without the filter its
        // QuantityRemaining equals its QuantityIn (both negative), it sails past the
        // already-consumed guard below, and `Consume(negative)` throws out of the Domain as a 500.
        var layers = await db.StockLedgerEntries
            .Where(x => x.OrganizationId == organizationId
                && x.SourceDocumentType == sourceDocumentType && x.SourceDocumentId == sourceDocumentId
                && x.QuantityIn > 0)
            .ToListAsync(cancellationToken);

        if (layers.Count == 0)
        {
            return;
        }

        if (layers.Any(x => x.QuantityRemaining != x.QuantityIn))
        {
            throw new ConflictException(
                "Cannot void this document -- some of the stock it added has already been consumed by a later document.");
        }

        foreach (var layer in layers)
        {
            // Phase 35b -- the reversal takes the layer's own location, never a fresh argument.
            // Same rule as a GL reversal follows: a release that landed somewhere else would
            // leave the original branch's stock value permanently off while the organization-wide
            // total still reconciled.
            //
            // Phase 51 -- BatchId and SerialNo join it under the same rule, and it is worth naming
            // why rather than letting the pattern carry it: a release that landed in a different
            // batch leaves that batch's derived quantity permanently wrong while the product-wide
            // total still reconciles, which is phase 43's failure exactly (giving Approve a new
            // warehouse source left Void restocking from the old one, so stock went out and never
            // came back).
            db.StockMovements.Add(StockMovement.Create(
                organizationId, layer.ProductId, layer.WarehouseId, StockMovementDirection.Out, layer.QuantityRemaining,
                layer.UnitCost, sourceDocumentType, sourceDocumentId, transactionDate, layer.LocationId,
                layer.BatchId, layer.SerialNo));

            layer.Consume(layer.QuantityRemaining);
        }
    }

    /// <summary>
    /// Phase 37 -- pays off this (product, warehouse)'s outstanding shortfall layers, oldest first,
    /// out of a receipt that has just been added as <paramref name="receipt"/>, and returns the
    /// <b>cost catch-up</b>: the amount by which stock on hand is now worth less than the receipt's
    /// own value implies.
    ///
    /// <para>Each filled unit was issued at the shortfall layer's assumed cost and has really cost
    /// <paramref name="unitCost"/>, so the catch-up is
    /// <c>filled x (unitCost - assumedCost)</c>, summed. It is positive when the covering receipt
    /// was dearer than assumed -- the usual case, since the assumption is a <i>previous</i>
    /// purchase price. The caller posts it to the ledger (see <c>StockCostCatchUp</c>) and a
    /// value-only <c>StockMovement</c> carries it into the dated stock reports, so all three views
    /// -- FIFO layers, general ledger and movement history -- move by the same number.</para>
    ///
    /// <para><b>Phase 51 -- same-batch debts first, un-batched debts second.</b> A receipt of batch
    /// B pays off what batch B owes before it pays off what the product owes generally, which is
    /// the obvious half. The second half is load-bearing and is the reason the order is written
    /// down rather than left to the date ordering: without it, an un-batched debt on a
    /// batch-tracked product could <i>never</i> be repaid by any receipt, because every receipt of
    /// such a product carries a batch. The debt would sit negative forever and the product's
    /// on-hand would be permanently understated.</para>
    /// </summary>
    private async Task<decimal> FillShortfallsAsync(
        Guid organizationId,
        Guid productId,
        Guid warehouseId,
        StockLedgerEntry receipt,
        decimal unitCost,
        DocumentType sourceDocumentType,
        Guid sourceDocumentId,
        DateOnly transactionDate,
        Guid? locationId,
        Guid? batchId,
        CancellationToken cancellationToken)
    {
        var shortfalls = await db.StockLedgerEntries
            .Where(x => x.OrganizationId == organizationId && x.ProductId == productId && x.WarehouseId == warehouseId
                && x.QuantityRemaining < 0)
            .OrderBy(x => x.TransactionDate)
            .ThenBy(x => x.CreatedAt)
            .ToListAsync(cancellationToken);

        if (shortfalls.Count == 0)
        {
            return 0m;
        }

        // A receipt may only fill a debt it can actually be the goods for: its own batch, or a debt
        // that belongs to no batch. A receipt of batch B must never quietly settle batch C.
        var eligible = shortfalls
            .Where(x => x.BatchId == batchId || x.BatchId is null)
            .OrderBy(x => x.BatchId == batchId ? 0 : 1)
            .ThenBy(x => x.TransactionDate)
            .ThenBy(x => x.CreatedAt)
            .ToList();

        var catchUp = 0m;

        foreach (var shortfall in eligible)
        {
            if (receipt.QuantityRemaining <= 0)
            {
                break;
            }

            var fill = Math.Min(receipt.QuantityRemaining, -shortfall.QuantityRemaining);
            shortfall.Fill(fill);
            receipt.Consume(fill);
            catchUp += fill * (unitCost - shortfall.UnitCost);
        }

        if (catchUp != 0)
        {
            db.StockMovements.Add(StockMovement.CreateCostAdjustment(
                organizationId, productId, warehouseId, catchUp,
                sourceDocumentType, sourceDocumentId, transactionDate, locationId, batchId));
        }

        return catchUp;
    }

    /// <summary>
    /// Phase 37 -- what a shortfall layer is worth until a real receipt says otherwise: the unit
    /// cost of the most recent layer for this (product, warehouse), by the same
    /// TransactionDate-then-CreatedAt ordering the FIFO walk uses, newest first. Zero when the
    /// product has never been received into this warehouse at all -- there is no price to guess
    /// from, and zero is the only figure that does not invent one. Shortfall layers themselves are
    /// excluded: chaining an assumption off an assumption would compound it.
    ///
    /// <para>Phase 51 -- when the request named a batch, the guess comes from that batch if it has
    /// ever been received here, and falls back to the product's own last cost if it has not. A
    /// brand-new batch of a long-stocked product is the common case and the product-level figure is
    /// a far better assumption for it than zero.</para>
    /// </summary>
    private async Task<decimal> LastKnownUnitCostAsync(
        Guid organizationId, Guid productId, Guid warehouseId, Guid? batchId, CancellationToken cancellationToken)
    {
        if (batchId is not null)
        {
            var batchCost = await db.StockLedgerEntries
                .Where(x => x.OrganizationId == organizationId && x.ProductId == productId
                    && x.WarehouseId == warehouseId && x.QuantityIn > 0 && x.BatchId == batchId)
                .OrderByDescending(x => x.TransactionDate)
                .ThenByDescending(x => x.CreatedAt)
                .Select(x => x.UnitCost)
                .FirstOrDefaultAsync(cancellationToken);

            if (batchCost != 0m)
            {
                return batchCost;
            }
        }

        return await db.StockLedgerEntries
            .Where(x => x.OrganizationId == organizationId && x.ProductId == productId && x.WarehouseId == warehouseId
                && x.QuantityIn > 0)
            .OrderByDescending(x => x.TransactionDate)
            .ThenByDescending(x => x.CreatedAt)
            .Select(x => x.UnitCost)
            .FirstOrDefaultAsync(cancellationToken);
    }

    private async Task<List<StockLedgerEntry>> LoadLayersOldestFirstAsync(
        Guid organizationId,
        Guid productId,
        Guid warehouseId,
        Guid? batchId,
        string? serialNo,
        CancellationToken cancellationToken)
    {
        var query = db.StockLedgerEntries
            .Where(x => x.OrganizationId == organizationId && x.ProductId == productId && x.WarehouseId == warehouseId
                && x.QuantityRemaining > 0);

        // Composed, not folded: see GetAvailableQuantityAsync's note on the non-short-circuiting
        // expression tree.
        if (batchId is not null)
        {
            query = query.Where(x => x.BatchId == batchId);
        }

        if (serialNo is not null)
        {
            query = query.Where(x => x.SerialNo == serialNo);
        }

        return await query
            .OrderBy(x => x.TransactionDate)
            .ThenBy(x => x.CreatedAt)
            .ToListAsync(cancellationToken);
    }
}
