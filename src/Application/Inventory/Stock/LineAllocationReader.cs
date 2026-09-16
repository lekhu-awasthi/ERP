using ErpApp.Application.Common.Persistence;
using ErpApp.Domain.Inventory;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.Inventory.Stock;

/// <summary>
/// Phase 51 -- the read side of <see cref="DocumentLineAllocationWriter"/>: turns a document's
/// stored <c>BatchId</c>s and serial rows back into what a detail DTO shows.
///
/// <para>One reader rather than one projection per detail query, because two detail queries
/// deriving the same figure separately is how divergence starts (phase 36: two reports agree only
/// through one shared reader plus a test that reads both on the same data). Here the "figure" is
/// only a batch number and a list of strings, but the argument is the same and the cost of sharing
/// is nil.</para>
/// </summary>
public static class LineAllocationReader
{
    public sealed record BatchSummary(string BatchNo, DateOnly? ManufactureDate, DateOnly? ExpiryDate);

    /// <summary>
    /// The batches named by <paramref name="batchIds"/>, keyed by id. A null or unknown id is
    /// simply absent, which is every line of every product that is not batch-tracked.
    /// </summary>
    public static async Task<IReadOnlyDictionary<Guid, BatchSummary>> LoadBatchesAsync(
        IAppDbContext db,
        Guid organizationId,
        IEnumerable<Guid?> batchIds,
        CancellationToken cancellationToken)
    {
        var ids = batchIds.Where(x => x is not null).Select(x => x!.Value).Distinct().ToList();

        if (ids.Count == 0)
        {
            return new Dictionary<Guid, BatchSummary>();
        }

        return await db.ProductBatches
            .Where(x => x.OrganizationId == organizationId && ids.Contains(x.Id))
            .Select(x => new { x.Id, Summary = new BatchSummary(x.BatchNo, x.ManufactureDate, x.ExpiryDate) })
            .ToDictionaryAsync(x => x.Id, x => x.Summary, cancellationToken);
    }

    /// <summary>The serial numbers each of <paramref name="lineIds"/> names, keyed by line id.</summary>
    public static Task<IReadOnlyDictionary<Guid, List<string>>> LoadSerialsAsync(
        IAppDbContext db,
        Guid organizationId,
        DocumentLineParentType parentType,
        IReadOnlyCollection<Guid> lineIds,
        CancellationToken cancellationToken) =>
        LineStockAllocator.LoadSerialsAsync(db, organizationId, parentType, lineIds, cancellationToken);
}
