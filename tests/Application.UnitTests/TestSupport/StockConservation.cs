using ErpApp.Application.Common.Persistence;
using ErpApp.Domain.Inventory;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace ErpApp.Application.UnitTests.TestSupport;

/// <summary>
/// Phase 37's acceptance test, in one place so several tests agree through it rather than each
/// asserting its own version of the same law (phase-26b's <c>ContactLedgerReader</c> lesson, and
/// phase 36's: two things patched into agreement agree by coincidence).
///
/// <para><b>The law.</b> Three views of stock value must move together at all times:</para>
/// <list type="number">
/// <item>the <b>FIFO layers</b> -- the sum of QuantityRemaining times UnitCost, shortfall layers
/// (negative on both counts, so a positive product) included;</item>
/// <item>the <b>general ledger</b> -- the balance of the tenant's Inventory account;</item>
/// <item>the <b>movement history</b> -- In value minus Out value, which is what every dated stock
/// report reconstructs from (phase 26c) and which now carries the cost catch-up as a value-only
/// row.</item>
/// </list>
///
/// <para>Phase 25 proved this law for manufacturing in SQL. Phase 37 is where it earns its keep,
/// because a shortfall layer is issued at an assumed cost and covered later at the real one: any
/// two of these three can be made to agree by patching one of them, and only asserting all three
/// together catches the third drifting.</para>
/// </summary>
internal static class StockConservation
{
    internal static async Task AssertHoldsAsync(IAppDbContext db, Guid organizationId)
    {
        var (layers, ledger, movements) = await MeasureAsync(db, organizationId);

        Assert.Equal(layers, ledger);
        Assert.Equal(layers, movements);
    }

    internal static async Task<(decimal Layers, decimal Ledger, decimal Movements)> MeasureAsync(
        IAppDbContext db, Guid organizationId)
    {
        var layerRows = await db.StockLedgerEntries
            .Where(x => x.OrganizationId == organizationId)
            .Select(x => new { x.QuantityRemaining, x.UnitCost })
            .ToListAsync(CancellationToken.None);
        var layers = layerRows.Sum(x => x.QuantityRemaining * x.UnitCost);

        var inventoryAccountId = await db.TenantSettings
            .Where(x => x.OrganizationId == organizationId)
            .Select(x => x.DefaultInventoryAccountId)
            .SingleAsync(CancellationToken.None)
            ?? throw new InvalidOperationException("The seed has no Inventory account.");

        var glRows = await (
            from line in db.GlLines
            join entry in db.GlJournalEntries on line.GlJournalEntryId equals entry.Id
            where entry.OrganizationId == organizationId && line.AccountId == inventoryAccountId
            select new { line.Debit, line.Credit }).ToListAsync(CancellationToken.None);
        var ledger = glRows.Sum(x => x.Debit - x.Credit);

        var movementRows = await db.StockMovements
            .Where(x => x.OrganizationId == organizationId)
            .Select(x => new { x.Direction, x.Quantity, x.UnitCost, x.ValueAdjustment })
            .ToListAsync(CancellationToken.None);
        var movements = movementRows.Sum(x =>
            ((x.Quantity * x.UnitCost) + x.ValueAdjustment) * (x.Direction == StockMovementDirection.In ? 1 : -1));

        return (layers, ledger, movements);
    }
}
