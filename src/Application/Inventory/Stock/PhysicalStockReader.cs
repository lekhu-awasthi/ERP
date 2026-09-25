using ErpApp.Application.Common.Persistence;
using ErpApp.Domain.Inventory;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.Inventory.Stock;

/// <summary>
/// Phase 58 -- the one place the <b>physical</b> stock ledger's balance is computed, and the only
/// thing that should read <c>PhysicalStockMovements</c> for a balance.
///
/// <para>The physical ledger is two sources folded into one: the rows the physical-only documents
/// write (<see cref="PhysicalStockMovement"/>), and the <see cref="StockMovement"/> rows of the
/// documents both ledgers count (<see cref="StockBooks.Shared"/>). Neither half is copied into the
/// other -- see <see cref="StockBooks"/> for why -- so every caller that wants "what is physically on
/// the shelf" has to add the two, and that addition lives here once. A Delivery Note's approval, a
/// GRN's void and the Inventory Variance Report's Actual Balance all read this, which is what makes
/// them agree by construction (phase 26b's shared-reader rule).</para>
///
/// <para><b>Two queries, added in memory.</b> The halves are different tables, and EF refuses a set
/// operation after a projection (phase 38), so each is grouped and summed on the server and the two
/// small per-product results are merged here. Each query is a <c>SUM(CASE ...)</c> over the
/// (OrganizationId, ProductId, WarehouseId, TransactionDate) composite both tables carry.</para>
/// </summary>
internal static class PhysicalStockReader
{
    /// <summary>
    /// The physical balance of each of <paramref name="productIds"/> in <paramref name="warehouseId"/>
    /// (every warehouse when null), counting everything dated on or before <paramref name="asOf"/>
    /// (everything when null). A product with no movement is absent from the result, not zero.
    /// </summary>
    internal static async Task<Dictionary<Guid, decimal>> GetOnHandAsync(
        IAppDbContext db,
        Guid organizationId,
        Guid? warehouseId,
        IReadOnlyCollection<Guid> productIds,
        CancellationToken cancellationToken,
        DateOnly? asOf = null)
    {
        var ids = productIds as IList<Guid> ?? productIds.ToList();

        var physical = db.PhysicalStockMovements
            .Where(m => m.OrganizationId == organizationId && ids.Contains(m.ProductId));
        var shared = db.StockMovements
            .Where(m => m.OrganizationId == organizationId && ids.Contains(m.ProductId))
            .Where(m => StockBooks.Shared.Contains(m.SourceDocumentType));

        if (warehouseId is { } warehouse)
        {
            physical = physical.Where(m => m.WarehouseId == warehouse);
            shared = shared.Where(m => m.WarehouseId == warehouse);
        }

        if (asOf is { } date)
        {
            physical = physical.Where(m => m.TransactionDate <= date);
            shared = shared.Where(m => m.TransactionDate <= date);
        }

        var fromPhysical = await physical
            .GroupBy(m => m.ProductId)
            .Select(g => new
            {
                ProductId = g.Key,
                Quantity = g.Sum(m => m.Direction == StockMovementDirection.In ? m.Quantity : -m.Quantity),
            })
            .ToListAsync(cancellationToken);

        // A cost catch-up row (phase 37) has zero quantity, so it adds nothing here -- the physical
        // ledger has no value to catch up.
        var fromShared = await shared
            .GroupBy(m => m.ProductId)
            .Select(g => new
            {
                ProductId = g.Key,
                Quantity = g.Sum(m => m.Direction == StockMovementDirection.In ? m.Quantity : -m.Quantity),
            })
            .ToListAsync(cancellationToken);

        var onHand = new Dictionary<Guid, decimal>();
        foreach (var row in fromPhysical.Concat(fromShared))
        {
            onHand[row.ProductId] = onHand.GetValueOrDefault(row.ProductId) + row.Quantity;
        }

        return onHand;
    }
}
