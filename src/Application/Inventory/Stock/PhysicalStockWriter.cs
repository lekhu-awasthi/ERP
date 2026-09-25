using ErpApp.Application.Common.Exceptions;
using ErpApp.Application.Common.Persistence;
using ErpApp.Domain.Catalog;
using ErpApp.Domain.Common;
using ErpApp.Domain.Inventory;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.Inventory.Stock;

/// <summary>
/// Phase 58 -- the one writer of <see cref="PhysicalStockMovement"/> rows, shared by the Delivery
/// Note's and the GRN's Approve and Void so that "what a physical document does to stock" is stated
/// once, and its undo beside it.
/// </summary>
internal static class PhysicalStockWriter
{
    /// <summary>One document line as the ledger sees it: a product and a primary-unit quantity.</summary>
    internal sealed record LineMovement(Guid ProductId, PrimaryQuantity Quantity);

    /// <summary>
    /// Writes one row per <b>Goods</b> line. A Service line moves nothing -- the same Product.Type
    /// gate every stock path in this codebase applies -- so a document of services alone writes no
    /// rows, and its void has nothing to undo.
    /// </summary>
    internal static async Task RecordAsync(
        IAppDbContext db,
        Guid organizationId,
        DocumentType sourceDocumentType,
        Guid sourceDocumentId,
        Guid warehouseId,
        DateOnly transactionDate,
        Guid? locationId,
        StockMovementDirection direction,
        IReadOnlyCollection<LineMovement> lines,
        CancellationToken cancellationToken)
    {
        var goods = await GoodsProductIdsAsync(db, organizationId, lines.Select(x => x.ProductId), cancellationToken);

        foreach (var line in lines.Where(x => goods.Contains(x.ProductId) && !x.Quantity.IsZero))
        {
            db.PhysicalStockMovements.Add(PhysicalStockMovement.Create(
                organizationId, line.ProductId, warehouseId, direction, line.Quantity,
                sourceDocumentType, sourceDocumentId, transactionDate, locationId));
        }
    }

    /// <summary>
    /// Writes the mirror of every row the document wrote, against the same source, dated
    /// <paramref name="transactionDate"/> (the document's own date, as every other void in this
    /// codebase dates its stock reversal -- so a voided document nets out of every dated report).
    /// </summary>
    /// <param name="refuseNegative">
    /// True for a <b>receipt</b>'s void. Taking back goods the warehouse no longer physically holds
    /// would drive the physical balance below zero with nothing on the shelf to show for it; the
    /// accounting ledger refuses the same thing for a consumed layer (phase 16a), whatever the
    /// Negative Item Balance setting says, and so does this. The reference product does not -- it
    /// voided a consumed GRN with Reject on and carried physical stock at -3 (2026-09-24) -- and
    /// this is a deliberate divergence: a void is an undo, not an issue, and an undo that manufactures
    /// a shortfall has not undone anything.
    /// </param>
    internal static async Task ReverseAsync(
        IAppDbContext db,
        Guid organizationId,
        DocumentType sourceDocumentType,
        Guid sourceDocumentId,
        DateOnly transactionDate,
        bool refuseNegative,
        CancellationToken cancellationToken)
    {
        var rows = await db.PhysicalStockMovements
            .Where(x => x.OrganizationId == organizationId
                && x.SourceDocumentType == sourceDocumentType
                && x.SourceDocumentId == sourceDocumentId)
            .ToListAsync(cancellationToken);

        if (rows.Count == 0)
        {
            return;
        }

        if (refuseNegative)
        {
            foreach (var warehouseGroup in rows.GroupBy(x => x.WarehouseId))
            {
                var taken = warehouseGroup
                    .GroupBy(x => x.ProductId)
                    .ToDictionary(
                        g => g.Key,
                        g => g.Sum(x => x.Direction == StockMovementDirection.In ? x.Quantity : -x.Quantity));

                var onHand = await PhysicalStockReader.GetOnHandAsync(
                    db, organizationId, warehouseGroup.Key, [.. taken.Keys], cancellationToken);

                if (taken.Any(t => onHand.GetValueOrDefault(t.Key) - t.Value < 0))
                {
                    throw new ConflictException(
                        "Cannot void this document -- some of the goods it received have already left the warehouse. "
                            + "Void the delivery notes or adjustments that took them first.");
                }
            }
        }

        db.PhysicalStockMovements.AddRange(rows.Select(x => x.Reverse(transactionDate)));
    }

    private static async Task<HashSet<Guid>> GoodsProductIdsAsync(
        IAppDbContext db, Guid organizationId, IEnumerable<Guid> productIds, CancellationToken cancellationToken)
    {
        var ids = productIds.Distinct().ToList();

        var goods = await db.Products
            .Where(x => x.OrganizationId == organizationId && ids.Contains(x.Id) && x.Type == ProductType.Goods)
            .Select(x => x.Id)
            .ToListAsync(cancellationToken);

        return [.. goods];
    }
}
