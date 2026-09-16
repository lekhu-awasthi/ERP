namespace ErpApp.Application.Inventory.Stock;

/// <summary>
/// Phase 51 -- what a <see cref="IStockLedgerService.ConsumeAsync"/> call actually took.
///
/// <para><b>Why the return type changed.</b> Consume returned a single weighted-average
/// <c>decimal</c>, which is enough for a COGS leg and not enough for anything that has to
/// <i>re-create</i> what it consumed. <c>ApproveWarehouseTransferCommandHandler</c> consumes from
/// one warehouse and increments the other at that average -- so for a batch-tracked product it
/// silently destroyed the batch identity (two batches out of Kathmandu, one un-batched layer into
/// Pokhara) and, as a pre-existing simplification nobody had cause to notice, collapsed two
/// genuinely different unit costs into one averaged layer.</para>
///
/// <para>This is one return type rather than a second <c>ConsumeDetailedAsync</c> method, because
/// CLAUDE.md's rule is to retire copies 1..N before writing copy N+1: two methods answering one
/// question is how <c>GrantedPermissionReader</c> ended up with two inlined joins that both missed
/// a filter.</para>
/// </summary>
/// <param name="AverageUnitCost">
/// The weighted-average cost of everything taken -- the figure every existing caller already read,
/// unchanged in meaning and in value. Zero when nothing was consumed.
/// </param>
/// <param name="Reliefs">
/// One entry per <c>(BatchId, SerialNo, UnitCost)</c> the walk took, in the order it took them,
/// including the shortfall (phase 37) when the layers could not cover the request.
/// </param>
public sealed record StockConsumption(decimal AverageUnitCost, IReadOnlyList<StockRelief> Reliefs)
{
    public static readonly StockConsumption None = new(0m, []);
}

/// <summary>
/// One layer's contribution to a consume: how much was taken, at what cost, from which batch and
/// which serial. A <see cref="Shortfall"/> relief is stock that was <i>not</i> there -- issued at
/// an assumed cost against a negative layer (phase 37) -- and a caller re-creating what it consumed
/// must never re-create one of those, because there was nothing to move.
/// </summary>
public sealed record StockRelief(
    decimal Quantity,
    decimal UnitCost,
    Guid? BatchId,
    string? SerialNo,
    bool Shortfall = false);
