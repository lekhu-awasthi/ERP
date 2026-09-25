using ErpApp.Application.Common.Locations;
using ErpApp.Application.Common.Persistence;
using ErpApp.Domain.Inventory;
using ErpApp.Domain.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.Inventory.Reports;

/// <summary>
/// The Opening / In / Out / Balance fact set that phase 26c's inventory reports agree through --
/// <c>InventoryPositionReport</c>, <c>InventoryMovementReport</c> and
/// <c>InventoryLedgerReport</c>'s bracket rows all read it, and so does Net Trading Assets'
/// Inventory Items row. This is phase-26b's <c>ContactLedgerReader</c> lesson applied to stock:
/// agreement between reports is a design property, not a coincidence. Inventory Position showing
/// the same figure as Inventory Movement's Balance columns is true <i>by construction</i> here,
/// because Position renders exactly the Balance triple this reader computes -- which is also what
/// the live reference product does (read side by side on 2026-09-03).
///
/// <para><b>Everything is derived from <see cref="StockMovement"/>, not from
/// <see cref="StockLedgerEntry"/>.</b> The FIFO layer table cannot answer a dated question:
/// <c>QuantityRemaining</c> is decremented <i>in place</i> as later documents consume a layer, so
/// it only ever describes stock as it stands right now. A report whose header says "for the period
/// ... to 30 Bhadra" and whose Balance column silently answered "as of today" would be wrong in the
/// one case a reader most needs it -- reopening a closed period. <c>StockMovement</c> is
/// append-only and carries the consuming document's own weighted-average unit cost, so
/// Opening+In-Out reconstructs both quantity and value at any date. At today's date the two agree,
/// which is what CLAUDE.md's "a live inventory value comes from QuantityRemaining x UnitCost"
/// gotcha is really asserting.</para>
///
/// <para><b>The one place the arithmetic deliberately stops.</b> When Balance quantity is zero or
/// negative, Balance value is reported as zero rather than as Opening+In-Out. Negative stock is an
/// error state -- goods sold that were never received -- and there is no cost to carry for goods
/// that are not there; inventing one would put a number in a Balance Sheet-adjacent column that no
/// purchase ever paid. The live report agrees: every negative-quantity row on 2026-09-03 printed
/// "-" in both its Rate and its Amount cells.</para>
///
/// <para><b>Phase 37 made that branch reachable, which is what it was written for.</b> Until then
/// <c>StockLedgerService.ConsumeAsync</c> <i>threw</i> a 409 whenever a document would consume more
/// than the layers hold, so no approval path could drive a balance below zero -- while the
/// reference product's "Negative Item Balance" setting (Reject / Warn / Do Nothing) let its own
/// tenant run warn-and-allow, which is why its Inventory Position had hundreds of negative rows and
/// ours could have none. That setting is now real: an oversell leaves a <b>shortfall layer</b>
/// behind and this report's negative rows print "-" for Rate and Amount, exactly as the live one
/// does. The guard cost one comparison and was right two phases before anything could test it.</para>
///
/// <para><b>The cost catch-up rides on this table too.</b> A shortfall is issued at an assumed cost
/// and covered later at the real one; the difference is written as a value-only movement (quantity
/// zero, <c>StockMovement.ValueAdjustment</c> non-zero), so a dated balance derived here moves by
/// the same amount the FIFO layers and the Inventory account do. Without it, In-minus-Out would
/// drift from both by exactly the catch-up from the fill onwards.</para>
///
/// <para>Each filter is applied as a concrete <c>Where</c> on the movement query, never through a
/// captured <c>Func</c> selector -- phase-9 bug #1.</para>
/// </summary>
internal static class StockFactReader
{
    /// <summary>
    /// One product's period, optionally narrowed to a single warehouse by the caller's filter.
    /// Quantities are signed the way a reader expects: <paramref name="InQuantity"/> and
    /// <paramref name="OutQuantity"/> are both non-negative magnitudes, and only
    /// <paramref name="OpeningQuantity"/>/<paramref name="BalanceQuantity"/> can go negative.
    /// </summary>
    internal sealed record ProductFacts(
        Guid ProductId,
        decimal OpeningQuantity,
        decimal OpeningValue,
        decimal InQuantity,
        decimal InValue,
        decimal OutQuantity,
        decimal OutValue,
        decimal BalanceQuantity,
        decimal BalanceValue);

    /// <summary>One movement row, for the kardex. Ordered oldest-first by the reader.</summary>
    internal sealed record Movement(
        Guid Id,
        Guid ProductId,
        Guid WarehouseId,
        DateOnly TransactionDate,
        DateTimeOffset CreatedAt,
        Domain.Common.DocumentType SourceDocumentType,
        Guid SourceDocumentId,
        StockMovementDirection Direction,
        decimal Quantity,
        decimal UnitCost,
        decimal ValueAdjustment = 0m)
    {
        /// <summary>
        /// Phase 37 -- a row is normally worth quantity times unit cost, and a cost-catch-up row is
        /// worth its <see cref="ValueAdjustment"/> with no quantity at all (see
        /// <c>StockMovement.ValueAdjustment</c>). Adding rather than branching keeps the two kinds
        /// indistinguishable to every caller: a movement report sums Value the same way either way,
        /// and the Rate a catch-up row implies is zero because its quantity is.
        /// </summary>
        public decimal Value => (Quantity * UnitCost) + ValueAdjustment;
    }

    /// <summary>
    /// The unit rate a value/quantity pair implies. Zero when there is no quantity to divide by --
    /// the screens render a zero rate as "-", which is what the live report prints.
    /// </summary>
    internal static decimal Rate(decimal value, decimal quantity) =>
        quantity == 0 ? 0 : value / quantity;

    /// <summary>
    /// Every movement for the organization up to and including <paramref name="toDate"/>, narrowed
    /// by the optional product and warehouse filters. Callers that need a product-category filter
    /// resolve it to a product-id list first (the category lives on <c>Product</c>, not on the
    /// movement) and pass it as <paramref name="productIds"/>.
    /// </summary>
    internal static async Task<List<Movement>> LoadMovementsAsync(
        IAppDbContext db,
        Guid organizationId,
        IReadOnlyCollection<Guid>? productIds,
        Guid? warehouseId,
        DateOnly toDate,
        CancellationToken cancellationToken,
        Guid? locationId = null,
        IReadOnlyList<Guid>? reportLocations = null)
    {
        // Phase 35b -- Billing Location and Warehouse are independent controls, confirmed live: the
        // Inventory Ledger screen carries both at once. So this composes onto the warehouse filter
        // rather than standing in for it.
        var query = db.StockMovements
            .Where(m => m.OrganizationId == organizationId && m.TransactionDate <= toDate)
            .AtLocations(locationId, reportLocations);

        if (productIds is not null)
        {
            var ids = productIds as IList<Guid> ?? productIds.ToList();
            query = query.Where(m => ids.Contains(m.ProductId));
        }

        if (warehouseId is { } warehouse)
        {
            query = query.Where(m => m.WarehouseId == warehouse);
        }

        var rows = await query
            .Select(m => new Movement(
                m.Id, m.ProductId, m.WarehouseId, m.TransactionDate, m.CreatedAt,
                m.SourceDocumentType, m.SourceDocumentId, m.Direction, m.Quantity, m.UnitCost, m.ValueAdjustment))
            .ToListAsync(cancellationToken);

        // CreatedAt is the tie-breaker for two movements sharing a TransactionDate, the same
        // deterministic ordering StockLedgerEntry documents for its own FIFO walk.
        return rows.OrderBy(m => m.TransactionDate).ThenBy(m => m.CreatedAt).ThenBy(m => m.Id).ToList();
    }

    /// <summary>
    /// Phase 58 -- the same movement list for the <b>physical</b> ledger: the rows only a Delivery
    /// Note or a GRN writes, plus the <see cref="Domain.Inventory.StockBooks.Shared"/> documents'
    /// <c>StockMovement</c> rows, with the same filters and the same ordering as
    /// <see cref="LoadMovementsAsync"/>. See <c>PhysicalStockReader</c> for why the ledger is these
    /// two sources and not a copy.
    ///
    /// <para><b>Quantity only.</b> Every row comes back with a zero unit cost and no value
    /// adjustment, so every Value a caller derives is zero. The physical ledger posts nothing and
    /// carries no cost of its own; letting the shared rows keep theirs would have produced a
    /// "value" that was the adjustments' cost and nobody else's -- a number with no meaning printed
    /// in a column that looks like it has one. Cost catch-up rows (quantity zero) are dropped.</para>
    ///
    /// <para>Two queries merged in memory rather than a SQL <c>Concat</c>: EF refuses a set operation
    /// after a client projection (phase 38).</para>
    /// </summary>
    internal static async Task<List<Movement>> LoadPhysicalMovementsAsync(
        IAppDbContext db,
        Guid organizationId,
        IReadOnlyCollection<Guid>? productIds,
        Guid? warehouseId,
        DateOnly toDate,
        CancellationToken cancellationToken,
        Guid? locationId = null,
        IReadOnlyList<Guid>? reportLocations = null)
    {
        var physical = db.PhysicalStockMovements
            .Where(m => m.OrganizationId == organizationId && m.TransactionDate <= toDate)
            .AtLocations(locationId, reportLocations);
        var shared = db.StockMovements
            .Where(m => m.OrganizationId == organizationId && m.TransactionDate <= toDate && m.Quantity != 0)
            .Where(m => StockBooks.Shared.Contains(m.SourceDocumentType))
            .AtLocations(locationId, reportLocations);

        if (productIds is not null)
        {
            var ids = productIds as IList<Guid> ?? productIds.ToList();
            physical = physical.Where(m => ids.Contains(m.ProductId));
            shared = shared.Where(m => ids.Contains(m.ProductId));
        }

        if (warehouseId is { } warehouse)
        {
            physical = physical.Where(m => m.WarehouseId == warehouse);
            shared = shared.Where(m => m.WarehouseId == warehouse);
        }

        var fromPhysical = await physical
            .Select(m => new Movement(
                m.Id, m.ProductId, m.WarehouseId, m.TransactionDate, m.CreatedAt,
                m.SourceDocumentType, m.SourceDocumentId, m.Direction, m.Quantity, 0m, 0m))
            .ToListAsync(cancellationToken);
        var fromShared = await shared
            .Select(m => new Movement(
                m.Id, m.ProductId, m.WarehouseId, m.TransactionDate, m.CreatedAt,
                m.SourceDocumentType, m.SourceDocumentId, m.Direction, m.Quantity, 0m, 0m))
            .ToListAsync(cancellationToken);

        return [.. fromPhysical.Concat(fromShared)
            .OrderBy(m => m.TransactionDate).ThenBy(m => m.CreatedAt).ThenBy(m => m.Id)];
    }

    /// <summary>
    /// Phase 58 -- the ledger a report reads when its caller did not choose one: the tenant's own
    /// Mode of Inventory Tracking, which is the live filter's default ("1 Selected", the tenant's
    /// mode, on every report that carries it).
    /// </summary>
    internal static async Task<InventoryTrackingMode> ResolveModeAsync(
        IAppDbContext db, Guid organizationId, InventoryTrackingMode? requested, CancellationToken cancellationToken) =>
        requested ?? await db.TenantSettings
            .Where(x => x.OrganizationId == organizationId)
            .Select(x => (InventoryTrackingMode?)x.InventoryTrackingMode)
            .SingleOrDefaultAsync(cancellationToken)
        // Nullable on purpose: PhysicalMovement is the enum's ordinal zero, so a bare default would
        // silently flip a tenant with no settings row onto the wrong ledger (phase-2 bug #2's shape).
        ?? InventoryTrackingMode.AccountingMovement;

    /// <summary>Phase 58 -- <see cref="LoadMovementsAsync"/> or <see cref="LoadPhysicalMovementsAsync"/>,
    /// by mode.</summary>
    internal static Task<List<Movement>> LoadMovementsAsync(
        IAppDbContext db,
        InventoryTrackingMode mode,
        Guid organizationId,
        IReadOnlyCollection<Guid>? productIds,
        Guid? warehouseId,
        DateOnly toDate,
        CancellationToken cancellationToken,
        Guid? locationId = null,
        IReadOnlyList<Guid>? reportLocations = null) =>
        mode == InventoryTrackingMode.PhysicalMovement
            ? LoadPhysicalMovementsAsync(db, organizationId, productIds, warehouseId, toDate, cancellationToken, locationId, reportLocations)
            : LoadMovementsAsync(db, organizationId, productIds, warehouseId, toDate, cancellationToken, locationId, reportLocations);

    /// <summary>
    /// Folds already-loaded movements into one <see cref="ProductFacts"/> per product. Movements
    /// dated before <paramref name="fromDate"/> become the opening position; the rest split into In
    /// and Out by direction.
    /// </summary>
    internal static List<ProductFacts> Summarise(IEnumerable<Movement> movements, DateOnly fromDate) =>
        movements
            .GroupBy(m => m.ProductId)
            .Select(group => Summarise(group.Key, group, fromDate))
            .ToList();

    internal static ProductFacts Summarise(Guid productId, IEnumerable<Movement> movements, DateOnly fromDate)
    {
        decimal openingQuantity = 0, openingValue = 0;
        decimal inQuantity = 0, inValue = 0;
        decimal outQuantity = 0, outValue = 0;

        foreach (var movement in movements)
        {
            var isIn = movement.Direction == StockMovementDirection.In;

            if (movement.TransactionDate < fromDate)
            {
                openingQuantity += isIn ? movement.Quantity : -movement.Quantity;
                openingValue += isIn ? movement.Value : -movement.Value;
                continue;
            }

            if (isIn)
            {
                inQuantity += movement.Quantity;
                inValue += movement.Value;
            }
            else
            {
                outQuantity += movement.Quantity;
                outValue += movement.Value;
            }
        }

        var balanceQuantity = openingQuantity + inQuantity - outQuantity;
        var balanceValue = balanceQuantity <= 0 ? 0 : openingValue + inValue - outValue;

        return new ProductFacts(
            productId,
            openingQuantity, openingValue,
            inQuantity, inValue,
            outQuantity, outValue,
            balanceQuantity, balanceValue);
    }
}
