using ErpApp.Application.Common.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.Inventory.Stock;

/// <summary>
/// Phase 92 -- which of a product's serials are in stock now, read through phase 51's filtered index
/// (<c>OrganizationId, ProductId, SerialNo</c> where in stock). That index was the only thing refusing a second receipt
/// of a unit already on the shelf, and it refused it as a <c>DbUpdateException</c>: a 500 naming nothing. A receipt
/// asks here first and answers a 400 (a draft) or a 409 (the ledger, for every path that puts a unit in stock).
///
/// <para>The answer is the rows' own serials, never the caller's: the match is the collation's, case-insensitive
/// (phase 91), so a caller naming <c>imei-2</c> is told about <c>IMEI-2</c>.</para>
/// </summary>
public static class SerialStock
{
    /// <summary>The serials among <paramref name="serials"/> that <paramref name="productId"/> holds in stock, in
    /// <paramref name="warehouseId"/> when one is given, else anywhere (the index is per product, not per
    /// warehouse: a unit is on one shelf at a time).</summary>
    public static async Task<IReadOnlyList<string>> InStockAsync(
        IAppDbContext db,
        Guid organizationId,
        Guid productId,
        IReadOnlyCollection<string> serials,
        CancellationToken cancellationToken,
        Guid? warehouseId = null)
    {
        var named = serials.Select(x => x.Trim()).Where(x => x.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (named.Count == 0)
        {
            return [];
        }

        var query = db.StockLedgerEntries.Where(x => x.OrganizationId == organizationId && x.ProductId == productId
            && x.SerialNo != null && named.Contains(x.SerialNo) && x.QuantityRemaining > 0);

        // A second Where, never `warehouseId == null || ...` (phase 33).
        if (warehouseId is { } warehouse)
        {
            query = query.Where(x => x.WarehouseId == warehouse);
        }

        return await query.Select(x => x.SerialNo!).Distinct().ToListAsync(cancellationToken);
    }

    /// <summary>
    /// The ledger's own question, asked before it writes a serial's layer: is this unit already in stock? Asked of
    /// the change tracker as well as the database, because a receipt and the consume before it share one save. A
    /// transfer takes IMEI-1 out of warehouse A (the tracked layer is at zero, the stored one is not yet) and puts it
    /// in B, and two lines of one bill naming the same unit both write before either is saved.
    /// </summary>
    internal static async Task<bool> IsInStockAsync(
        IAppDbContext db, Guid organizationId, Guid productId, string serialNo, CancellationToken cancellationToken)
    {
        // Loaded rather than counted: identity resolution hands back the tracked instance, whose QuantityRemaining is
        // what this unit of work has made it, and that is the figure the check below reads.
        await db.StockLedgerEntries
            .Where(x => x.OrganizationId == organizationId && x.ProductId == productId && x.SerialNo == serialNo
                && x.QuantityRemaining > 0)
            .LoadAsync(cancellationToken);

        return db.StockLedgerEntries.Local.Any(x => x.OrganizationId == organizationId && x.ProductId == productId
            && x.SerialNo is not null && string.Equals(x.SerialNo, serialNo, StringComparison.OrdinalIgnoreCase)
            && x.QuantityRemaining > 0);
    }
}
