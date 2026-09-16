using ErpApp.Application.Common.Persistence;
using ErpApp.Domain.Common;
using ErpApp.Domain.Inventory;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.Inventory.Stock;

/// <summary>
/// Phase 51 -- moves one <b>document line's</b> worth of stock, honouring whatever batch and serial
/// numbers that line names.
///
/// <para><b>Why it exists.</b> A serial is a layer of quantity one, so a serialised line of quantity
/// five is five <c>IStockLedgerService</c> calls rather than one. That split has to happen
/// somewhere, and putting it in each of the handlers would be four copies of a loop that has to
/// agree about the weighted average it reports back -- which is exactly the shape CLAUDE.md warns
/// about (<i>retire copies 1..N before writing copy N+1</i>). It happens here, once, and the
/// handlers keep reading a single average cost the way they always have.</para>
///
/// <para>Nothing here reads the tenant's tracking flags: a line that names no batch and no serials
/// takes precisely the path it took before this phase, down to the number of rows written.
/// <c>StockTrackingRules</c> is what decides whether a line was allowed to be in that state.</para>
/// </summary>
public static class LineStockAllocator
{
    /// <summary>
    /// The serial numbers named by each of <paramref name="lineIds"/>, keyed by line id. Absent
    /// from the dictionary means the line named none, which is every line of every product whose
    /// Serial Number Tracking flag is off.
    /// </summary>
    public static async Task<IReadOnlyDictionary<Guid, List<string>>> LoadSerialsAsync(
        IAppDbContext db,
        Guid organizationId,
        DocumentLineParentType parentType,
        IReadOnlyCollection<Guid> lineIds,
        CancellationToken cancellationToken)
    {
        if (lineIds.Count == 0)
        {
            return new Dictionary<Guid, List<string>>();
        }

        var rows = await db.DocumentLineSerials
            .Where(x => x.OrganizationId == organizationId && x.ParentType == parentType
                && lineIds.Contains(x.ParentLineId))
            .OrderBy(x => x.CreatedAt)
            .Select(x => new { x.ParentLineId, x.SerialNo })
            .ToListAsync(cancellationToken);

        return rows
            .GroupBy(x => x.ParentLineId)
            .ToDictionary(g => g.Key, g => g.Select(x => x.SerialNo).ToList());
    }

    /// <summary>
    /// Consumes one line. Returns the weighted-average unit cost of everything taken -- the COGS
    /// figure the caller multiplies back by the line quantity, unchanged in meaning from before
    /// this phase -- together with the reliefs, for a caller that has to re-create what it consumed
    /// (see <c>ApproveWarehouseTransferCommandHandler</c>).
    /// </summary>
    public static async Task<StockConsumption> ConsumeLineAsync(
        IStockLedgerService ledger,
        Guid organizationId,
        Guid productId,
        Guid warehouseId,
        decimal quantity,
        Guid? batchId,
        IReadOnlyCollection<string> serialNumbers,
        DocumentType sourceDocumentType,
        Guid sourceDocumentId,
        DateOnly transactionDate,
        CancellationToken cancellationToken,
        Guid? locationId = null,
        bool allowNegative = false)
    {
        if (serialNumbers.Count == 0)
        {
            return await ledger.ConsumeAsync(
                organizationId, productId, warehouseId, quantity, sourceDocumentType, sourceDocumentId,
                transactionDate, cancellationToken, locationId, allowNegative, batchId);
        }

        // One physical unit at a time. allowNegative is deliberately not forwarded: ConsumeAsync
        // refuses a shortfall for a named serial whatever it is handed, and passing it would read as
        // though the setting had a say here.
        var reliefs = new List<StockRelief>();
        var totalCost = 0m;

        foreach (var serialNo in serialNumbers)
        {
            var one = await ledger.ConsumeAsync(
                organizationId, productId, warehouseId, 1m, sourceDocumentType, sourceDocumentId,
                transactionDate, cancellationToken, locationId, allowNegative: false, batchId, serialNo);

            reliefs.AddRange(one.Reliefs);
            totalCost += one.AverageUnitCost;
        }

        return new StockConsumption(totalCost / serialNumbers.Count, reliefs);
    }

    /// <summary>
    /// Receives one line. Returns the phase-37 cost catch-up, summed across the units received --
    /// which a caller must post, or the Inventory account drifts from the FIFO layers by exactly
    /// this figure.
    /// </summary>
    public static async Task<decimal> IncrementLineAsync(
        IStockLedgerService ledger,
        Guid organizationId,
        Guid productId,
        Guid warehouseId,
        decimal quantity,
        decimal unitCost,
        Guid? batchId,
        IReadOnlyCollection<string> serialNumbers,
        DocumentType sourceDocumentType,
        Guid sourceDocumentId,
        DateOnly transactionDate,
        CancellationToken cancellationToken,
        Guid? locationId = null)
    {
        if (serialNumbers.Count == 0)
        {
            return await ledger.IncrementAsync(
                organizationId, productId, warehouseId, quantity, unitCost, sourceDocumentType,
                sourceDocumentId, transactionDate, cancellationToken, locationId, batchId);
        }

        var catchUp = 0m;

        foreach (var serialNo in serialNumbers)
        {
            catchUp += await ledger.IncrementAsync(
                organizationId, productId, warehouseId, 1m, unitCost, sourceDocumentType,
                sourceDocumentId, transactionDate, cancellationToken, locationId, batchId, serialNo);
        }

        return catchUp;
    }
}
