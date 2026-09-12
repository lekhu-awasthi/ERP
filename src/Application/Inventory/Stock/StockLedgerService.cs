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
        decimal quantity,
        decimal unitCost,
        DocumentType sourceDocumentType,
        Guid sourceDocumentId,
        DateOnly transactionDate,
        CancellationToken cancellationToken,
        Guid? locationId = null)
    {
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
            transactionDate, locationId);
        db.StockLedgerEntries.Add(entry);

        db.StockMovements.Add(StockMovement.Create(
            organizationId, productId, warehouseId, StockMovementDirection.In, quantity, unitCost,
            sourceDocumentType, sourceDocumentId, transactionDate, locationId));

        // Phase 37 -- a receipt pays off what is owed before it becomes stock on hand. The new
        // layer is created at its full size first (above) and then consumed down by the fill, which
        // is what keeps ReverseIncrementAsync's "QuantityRemaining != QuantityIn means someone has
        // already taken this" guard true: a receipt that covered a shortfall cannot be voided, and
        // should not be.
        var catchUp = await FillShortfallsAsync(
            organizationId, productId, warehouseId, entry, unitCost, sourceDocumentType, sourceDocumentId,
            transactionDate, locationId, cancellationToken);

        return catchUp;
    }

    public async Task<decimal> ConsumeAsync(
        Guid organizationId,
        Guid productId,
        Guid warehouseId,
        decimal quantity,
        DocumentType sourceDocumentType,
        Guid sourceDocumentId,
        DateOnly transactionDate,
        CancellationToken cancellationToken,
        Guid? locationId = null,
        bool allowNegative = false)
    {
        if (quantity < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(quantity), "Quantity cannot be negative.");
        }

        if (quantity == 0)
        {
            return 0m;
        }

        var layers = await LoadLayersOldestFirstAsync(organizationId, productId, warehouseId, cancellationToken);

        var totalAvailable = layers.Sum(x => x.QuantityRemaining);
        if (quantity > totalAvailable && !allowNegative)
        {
            throw new ConflictException(
                $"Cannot consume {quantity} unit(s) of this product from this warehouse -- only {totalAvailable} remain in stock.");
        }

        var remainingToConsume = quantity;
        var totalCost = 0m;

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
        }

        // Phase 37 -- whatever the layers could not cover becomes a shortfall layer at the
        // product's last known cost in this warehouse. The cost is an assumption and is corrected
        // at fill time (see FillShortfallsAsync); what matters here is that the ledger's value
        // moves by exactly what this method reports back, so the caller's GL leg and the ledger
        // agree the moment they are both written.
        if (remainingToConsume > 0)
        {
            var assumedUnitCost = await LastKnownUnitCostAsync(
                organizationId, productId, warehouseId, cancellationToken);

            db.StockLedgerEntries.Add(StockLedgerEntry.CreateShortfall(
                organizationId, productId, warehouseId, remainingToConsume, assumedUnitCost,
                sourceDocumentType, sourceDocumentId, transactionDate, locationId));

            totalCost += remainingToConsume * assumedUnitCost;
        }

        var averageUnitCost = totalCost / quantity;

        db.StockMovements.Add(StockMovement.Create(
            organizationId, productId, warehouseId, StockMovementDirection.Out, quantity, averageUnitCost,
            sourceDocumentType, sourceDocumentId, transactionDate, locationId));

        return averageUnitCost;
    }

    public async Task<decimal> GetAvailableQuantityAsync(
        Guid organizationId, Guid productId, Guid warehouseId, CancellationToken cancellationToken)
    {
        return await db.StockLedgerEntries
            .Where(x => x.OrganizationId == organizationId && x.ProductId == productId && x.WarehouseId == warehouseId)
            .SumAsync(x => x.QuantityRemaining, cancellationToken);
    }

    public async Task<decimal> PreviewConsumptionCostAsync(
        Guid organizationId, Guid productId, Guid warehouseId, decimal quantity, CancellationToken cancellationToken)
    {
        if (quantity <= 0)
        {
            return 0m;
        }

        var layers = await LoadLayersOldestFirstAsync(organizationId, productId, warehouseId, cancellationToken);

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
            db.StockMovements.Add(StockMovement.Create(
                organizationId, layer.ProductId, layer.WarehouseId, StockMovementDirection.Out, layer.QuantityRemaining,
                layer.UnitCost, sourceDocumentType, sourceDocumentId, transactionDate, layer.LocationId));

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

        var catchUp = 0m;

        foreach (var shortfall in shortfalls)
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
                sourceDocumentType, sourceDocumentId, transactionDate, locationId));
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
    /// </summary>
    private async Task<decimal> LastKnownUnitCostAsync(
        Guid organizationId, Guid productId, Guid warehouseId, CancellationToken cancellationToken)
    {
        return await db.StockLedgerEntries
            .Where(x => x.OrganizationId == organizationId && x.ProductId == productId && x.WarehouseId == warehouseId
                && x.QuantityIn > 0)
            .OrderByDescending(x => x.TransactionDate)
            .ThenByDescending(x => x.CreatedAt)
            .Select(x => x.UnitCost)
            .FirstOrDefaultAsync(cancellationToken);
    }

    private async Task<List<StockLedgerEntry>> LoadLayersOldestFirstAsync(
        Guid organizationId, Guid productId, Guid warehouseId, CancellationToken cancellationToken)
    {
        return await db.StockLedgerEntries
            .Where(x => x.OrganizationId == organizationId && x.ProductId == productId && x.WarehouseId == warehouseId
                && x.QuantityRemaining > 0)
            .OrderBy(x => x.TransactionDate)
            .ThenBy(x => x.CreatedAt)
            .ToListAsync(cancellationToken);
    }
}
