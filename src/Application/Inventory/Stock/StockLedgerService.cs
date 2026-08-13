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
    public Task IncrementAsync(
        Guid organizationId,
        Guid productId,
        Guid warehouseId,
        decimal quantity,
        decimal unitCost,
        DocumentType sourceDocumentType,
        Guid sourceDocumentId,
        DateOnly transactionDate,
        CancellationToken cancellationToken)
    {
        if (quantity < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(quantity), "Quantity cannot be negative.");
        }

        if (quantity == 0)
        {
            return Task.CompletedTask;
        }

        var entry = StockLedgerEntry.Create(
            organizationId, productId, warehouseId, quantity, unitCost, sourceDocumentType, sourceDocumentId, transactionDate);
        db.StockLedgerEntries.Add(entry);

        db.StockMovements.Add(StockMovement.Create(
            organizationId, productId, warehouseId, StockMovementDirection.In, quantity, unitCost,
            sourceDocumentType, sourceDocumentId, transactionDate));

        return Task.CompletedTask;
    }

    public async Task<decimal> ConsumeAsync(
        Guid organizationId,
        Guid productId,
        Guid warehouseId,
        decimal quantity,
        DocumentType sourceDocumentType,
        Guid sourceDocumentId,
        DateOnly transactionDate,
        CancellationToken cancellationToken)
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
        if (quantity > totalAvailable)
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

        var averageUnitCost = totalCost / quantity;

        db.StockMovements.Add(StockMovement.Create(
            organizationId, productId, warehouseId, StockMovementDirection.Out, quantity, averageUnitCost,
            sourceDocumentType, sourceDocumentId, transactionDate));

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
