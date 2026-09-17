using ErpApp.Application.Common.Exceptions;
using ErpApp.Application.Inventory.Stock;
using ErpApp.Application.UnitTests.TestSupport;
using ErpApp.Domain.Common;
using ErpApp.Domain.Inventory;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.UnitTests.Inventory;

/// <summary>
/// Phase 51 — the batch and serial dimensions in the FIFO engine, tested where phase 7 tested the
/// engine itself: against <c>IStockLedgerService</c> directly, no MediatR handler involved.
///
/// <para><b>The class these tests belong to is the conservation law</b>, which is the phase's stated
/// acceptance test (phase 25's rule, applied to a dimension rather than to a value transformation).
/// Stated for this phase: <i>the sum of a product's layers equals the sum of its batches' layers
/// plus its un-batched layers, always, because the batch is a GROUP BY over the one quantity and not
/// a second one.</i> That cannot be violated by construction, which is the argument for the model —
/// so what these tests actually check is that the engine never writes a layer whose key is wrong,
/// which is the only way the grouping could lie.</para>
///
/// <para>InMemory does not enforce the filtered unique index that makes a serial unique among
/// in-stock layers, so that half is verified against real SQL Server in the manual E2E (phase 20e
/// Decision C's discipline) and pinned structurally in <c>Infrastructure.UnitTests</c>.</para>
/// </summary>
public class BatchAndSerialLedgerTests
{
    private static readonly Guid OrganizationId = Guid.NewGuid();
    private static readonly Guid ProductId = Guid.NewGuid();
    private static readonly Guid WarehouseId = Guid.NewGuid();
    private static readonly Guid OtherWarehouseId = Guid.NewGuid();
    private static readonly Guid BatchA = Guid.NewGuid();
    private static readonly Guid BatchB = Guid.NewGuid();

    private static readonly DateOnly Day1 = new(2026, 1, 1);
    private static readonly DateOnly Day2 = new(2026, 1, 2);
    private static readonly DateOnly Day3 = new(2026, 1, 3);

    [Fact]
    public async Task A_named_batch_narrows_the_fifo_walk_to_that_batch()
    {
        var db = TestAppDbContext.Create();
        var service = new StockLedgerService(db);

        // Batch A is older AND cheaper, so an unnarrowed FIFO walk would take it first.
        await service.IncrementAsync(
            OrganizationId, ProductId, WarehouseId, PrimaryQuantity.AlreadyPrimary(10m), 5m, DocumentType.PurchaseBill, Guid.NewGuid(),
            Day1, CancellationToken.None, batchId: BatchA);
        await service.IncrementAsync(
            OrganizationId, ProductId, WarehouseId, PrimaryQuantity.AlreadyPrimary(10m), 8m, DocumentType.PurchaseBill, Guid.NewGuid(),
            Day2, CancellationToken.None, batchId: BatchB);
        await db.SaveChangesAsync(CancellationToken.None);

        var consumption = await service.ConsumeAsync(
            OrganizationId, ProductId, WarehouseId, PrimaryQuantity.AlreadyPrimary(4m), DocumentType.Invoice, Guid.NewGuid(),
            Day3, CancellationToken.None, batchId: BatchB);
        await db.SaveChangesAsync(CancellationToken.None);

        // Batch B's cost, not batch A's -- which is the whole point of naming a batch.
        Assert.Equal(8m, consumption.AverageUnitCost);

        var layers = await db.StockLedgerEntries.ToListAsync();
        Assert.Equal(10m, layers.Single(x => x.BatchId == BatchA).QuantityRemaining);
        Assert.Equal(6m, layers.Single(x => x.BatchId == BatchB).QuantityRemaining);
    }

    [Fact]
    public async Task An_unnamed_batch_walks_every_batch_oldest_first()
    {
        var db = TestAppDbContext.Create();
        var service = new StockLedgerService(db);

        await service.IncrementAsync(
            OrganizationId, ProductId, WarehouseId, PrimaryQuantity.AlreadyPrimary(10m), 5m, DocumentType.PurchaseBill, Guid.NewGuid(),
            Day1, CancellationToken.None, batchId: BatchA);
        await service.IncrementAsync(
            OrganizationId, ProductId, WarehouseId, PrimaryQuantity.AlreadyPrimary(10m), 8m, DocumentType.PurchaseBill, Guid.NewGuid(),
            Day2, CancellationToken.None, batchId: BatchB);
        await db.SaveChangesAsync(CancellationToken.None);

        // Leaving the batch blank on an issue is legal and is what makes the seven document types
        // the read never showed a control on keep working.
        var consumption = await service.ConsumeAsync(
            OrganizationId, ProductId, WarehouseId, PrimaryQuantity.AlreadyPrimary(14m), DocumentType.Invoice, Guid.NewGuid(),
            Day3, CancellationToken.None);
        await db.SaveChangesAsync(CancellationToken.None);

        // 10 @ 5 from A, then 4 @ 8 from B = 82 over 14.
        Assert.Equal(82m / 14m, consumption.AverageUnitCost);

        // One relief per batch touched, which is what makes the movement rows attributable.
        Assert.Equal(2, consumption.Reliefs.Count);
        Assert.Equal(BatchA, consumption.Reliefs[0].BatchId);
        Assert.Equal(BatchB, consumption.Reliefs[1].BatchId);
    }

    [Fact]
    public async Task One_movement_row_is_written_per_batch_touched_not_per_call()
    {
        var db = TestAppDbContext.Create();
        var service = new StockLedgerService(db);

        await service.IncrementAsync(
            OrganizationId, ProductId, WarehouseId, PrimaryQuantity.AlreadyPrimary(10m), 5m, DocumentType.PurchaseBill, Guid.NewGuid(),
            Day1, CancellationToken.None, batchId: BatchA);
        await service.IncrementAsync(
            OrganizationId, ProductId, WarehouseId, PrimaryQuantity.AlreadyPrimary(10m), 8m, DocumentType.PurchaseBill, Guid.NewGuid(),
            Day2, CancellationToken.None, batchId: BatchB);
        await db.SaveChangesAsync(CancellationToken.None);

        var invoiceId = Guid.NewGuid();
        await service.ConsumeAsync(
            OrganizationId, ProductId, WarehouseId, PrimaryQuantity.AlreadyPrimary(14m), DocumentType.Invoice, invoiceId,
            Day3, CancellationToken.None);
        await db.SaveChangesAsync(CancellationToken.None);

        var outRows = await db.StockMovements
            .Where(x => x.SourceDocumentId == invoiceId && x.Direction == StockMovementDirection.Out)
            .ToListAsync();

        Assert.Equal(2, outRows.Count);
        Assert.Equal(10m, outRows.Single(x => x.BatchId == BatchA).Quantity);
        Assert.Equal(4m, outRows.Single(x => x.BatchId == BatchB).Quantity);

        // The conservation check: the movement rows account for exactly what the layers lost.
        Assert.Equal(14m, outRows.Sum(x => x.Quantity));
    }

    [Fact]
    public async Task An_untracked_product_writes_exactly_one_movement_row_as_it_always_did()
    {
        var db = TestAppDbContext.Create();
        var service = new StockLedgerService(db);

        // Two layers, no batch anywhere -- the pre-phase-51 world, which must be bit-for-bit
        // unchanged or this phase is not additive.
        await service.IncrementAsync(
            OrganizationId, ProductId, WarehouseId, PrimaryQuantity.AlreadyPrimary(10m), 5m, DocumentType.PurchaseBill, Guid.NewGuid(),
            Day1, CancellationToken.None);
        await service.IncrementAsync(
            OrganizationId, ProductId, WarehouseId, PrimaryQuantity.AlreadyPrimary(10m), 8m, DocumentType.PurchaseBill, Guid.NewGuid(),
            Day2, CancellationToken.None);
        await db.SaveChangesAsync(CancellationToken.None);

        var invoiceId = Guid.NewGuid();
        await service.ConsumeAsync(
            OrganizationId, ProductId, WarehouseId, PrimaryQuantity.AlreadyPrimary(14m), DocumentType.Invoice, invoiceId,
            Day3, CancellationToken.None);
        await db.SaveChangesAsync(CancellationToken.None);

        var outRows = await db.StockMovements
            .Where(x => x.SourceDocumentId == invoiceId && x.Direction == StockMovementDirection.Out)
            .ToListAsync();

        Assert.Single(outRows);
        Assert.Equal(14m, outRows[0].Quantity);
        Assert.Equal(82m / 14m, outRows[0].UnitCost);
        Assert.Null(outRows[0].BatchId);
    }

    [Fact]
    public async Task A_shortfall_carries_the_batch_the_request_named()
    {
        var db = TestAppDbContext.Create();
        var service = new StockLedgerService(db);

        await service.IncrementAsync(
            OrganizationId, ProductId, WarehouseId, PrimaryQuantity.AlreadyPrimary(4m), 5m, DocumentType.PurchaseBill, Guid.NewGuid(),
            Day1, CancellationToken.None, batchId: BatchA);
        await db.SaveChangesAsync(CancellationToken.None);

        await service.ConsumeAsync(
            OrganizationId, ProductId, WarehouseId, PrimaryQuantity.AlreadyPrimary(10m), DocumentType.Invoice, Guid.NewGuid(),
            Day2, CancellationToken.None, allowNegative: true, batchId: BatchA);
        await db.SaveChangesAsync(CancellationToken.None);

        var shortfall = await db.StockLedgerEntries.SingleAsync(x => x.QuantityIn < 0);

        // The batch that was asked for and came up short owes the debt -- not the product generally.
        Assert.Equal(BatchA, shortfall.BatchId);
        Assert.Equal(-6m, shortfall.QuantityRemaining);
    }

    [Fact]
    public async Task A_shortfall_with_no_batch_named_belongs_to_no_batch()
    {
        var db = TestAppDbContext.Create();
        var service = new StockLedgerService(db);

        // Units that were never received belong to no batch, because no receipt created one.
        await service.ConsumeAsync(
            OrganizationId, ProductId, WarehouseId, PrimaryQuantity.AlreadyPrimary(3m), DocumentType.Invoice, Guid.NewGuid(),
            Day2, CancellationToken.None, allowNegative: true);
        await db.SaveChangesAsync(CancellationToken.None);

        var shortfall = await db.StockLedgerEntries.SingleAsync(x => x.QuantityIn < 0);
        Assert.Null(shortfall.BatchId);
    }

    [Fact]
    public async Task A_receipt_pays_its_own_batch_debt_before_an_unbatched_one()
    {
        var db = TestAppDbContext.Create();
        var service = new StockLedgerService(db);

        // Two debts: one against batch A, one against no batch at all.
        await service.ConsumeAsync(
            OrganizationId, ProductId, WarehouseId, PrimaryQuantity.AlreadyPrimary(5m), DocumentType.Invoice, Guid.NewGuid(),
            Day1, CancellationToken.None, allowNegative: true, batchId: BatchA);
        await service.ConsumeAsync(
            OrganizationId, ProductId, WarehouseId, PrimaryQuantity.AlreadyPrimary(5m), DocumentType.Invoice, Guid.NewGuid(),
            Day1, CancellationToken.None, allowNegative: true);
        await db.SaveChangesAsync(CancellationToken.None);

        // A receipt of batch A, big enough for one debt and not both.
        await service.IncrementAsync(
            OrganizationId, ProductId, WarehouseId, PrimaryQuantity.AlreadyPrimary(5m), 7m, DocumentType.PurchaseBill, Guid.NewGuid(),
            Day2, CancellationToken.None, batchId: BatchA);
        await db.SaveChangesAsync(CancellationToken.None);

        var layers = await db.StockLedgerEntries.ToListAsync();

        // Batch A's debt is settled; the un-batched one is untouched.
        Assert.Equal(0m, layers.Single(x => x.QuantityIn == -5m && x.BatchId == BatchA).QuantityRemaining);
        Assert.Equal(-5m, layers.Single(x => x.QuantityIn == -5m && x.BatchId == null).QuantityRemaining);
    }

    [Fact]
    public async Task A_receipt_of_one_batch_can_still_settle_an_unbatched_debt()
    {
        var db = TestAppDbContext.Create();
        var service = new StockLedgerService(db);

        // This is the load-bearing half of the fill order. Every receipt of a batch-tracked product
        // carries a batch, so without it an un-batched debt could never be repaid by anything and
        // the product's on-hand would be understated forever.
        await service.ConsumeAsync(
            OrganizationId, ProductId, WarehouseId, PrimaryQuantity.AlreadyPrimary(4m), DocumentType.Invoice, Guid.NewGuid(),
            Day1, CancellationToken.None, allowNegative: true);
        await db.SaveChangesAsync(CancellationToken.None);

        await service.IncrementAsync(
            OrganizationId, ProductId, WarehouseId, PrimaryQuantity.AlreadyPrimary(4m), 6m, DocumentType.PurchaseBill, Guid.NewGuid(),
            Day2, CancellationToken.None, batchId: BatchA);
        await db.SaveChangesAsync(CancellationToken.None);

        var debt = await db.StockLedgerEntries.SingleAsync(x => x.QuantityIn < 0);
        Assert.Equal(0m, debt.QuantityRemaining);
    }

    [Fact]
    public async Task A_receipt_of_one_batch_never_settles_another_batchs_debt()
    {
        var db = TestAppDbContext.Create();
        var service = new StockLedgerService(db);

        await service.ConsumeAsync(
            OrganizationId, ProductId, WarehouseId, PrimaryQuantity.AlreadyPrimary(4m), DocumentType.Invoice, Guid.NewGuid(),
            Day1, CancellationToken.None, allowNegative: true, batchId: BatchA);
        await db.SaveChangesAsync(CancellationToken.None);

        await service.IncrementAsync(
            OrganizationId, ProductId, WarehouseId, PrimaryQuantity.AlreadyPrimary(4m), 6m, DocumentType.PurchaseBill, Guid.NewGuid(),
            Day2, CancellationToken.None, batchId: BatchB);
        await db.SaveChangesAsync(CancellationToken.None);

        var debt = await db.StockLedgerEntries.SingleAsync(x => x.QuantityIn < 0);

        // Batch B's goods are not batch A's goods, whatever the arithmetic says.
        Assert.Equal(-4m, debt.QuantityRemaining);
        Assert.Equal(4m, (await db.StockLedgerEntries.SingleAsync(x => x.BatchId == BatchB)).QuantityRemaining);
    }

    // ---------------------------------------------------------------------------- serials

    [Fact]
    public async Task A_serial_is_relieved_specifically_even_when_an_older_layer_exists()
    {
        var db = TestAppDbContext.Create();
        var service = new StockLedgerService(db);

        // A1 is older and cheaper; FIFO would take it. Specific identification must not.
        await service.IncrementAsync(
            OrganizationId, ProductId, WarehouseId, PrimaryQuantity.AlreadyPrimary(1m), 100m, DocumentType.PurchaseBill, Guid.NewGuid(),
            Day1, CancellationToken.None, serialNo: "A1");
        await service.IncrementAsync(
            OrganizationId, ProductId, WarehouseId, PrimaryQuantity.AlreadyPrimary(1m), 250m, DocumentType.PurchaseBill, Guid.NewGuid(),
            Day2, CancellationToken.None, serialNo: "J9");
        await db.SaveChangesAsync(CancellationToken.None);

        var consumption = await service.ConsumeAsync(
            OrganizationId, ProductId, WarehouseId, PrimaryQuantity.AlreadyPrimary(1m), DocumentType.Invoice, Guid.NewGuid(),
            Day3, CancellationToken.None, serialNo: "J9");
        await db.SaveChangesAsync(CancellationToken.None);

        Assert.Equal(250m, consumption.AverageUnitCost);

        var layers = await db.StockLedgerEntries.ToListAsync();
        Assert.Equal(1m, layers.Single(x => x.SerialNo == "A1").QuantityRemaining);
        Assert.Equal(0m, layers.Single(x => x.SerialNo == "J9").QuantityRemaining);
    }

    [Fact]
    public async Task A_serial_that_is_not_in_stock_is_refused_whatever_the_negative_setting_says()
    {
        var db = TestAppDbContext.Create();
        var service = new StockLedgerService(db);

        // allowNegative: true is the tenant saying "let me sell what I have not booked in".
        // Naming a physical unit that was never received is not that; it is a typo.
        var ex = await Assert.ThrowsAsync<ConflictException>(() => service.ConsumeAsync(
            OrganizationId, ProductId, WarehouseId, PrimaryQuantity.AlreadyPrimary(1m), DocumentType.Invoice, Guid.NewGuid(),
            Day2, CancellationToken.None, allowNegative: true, serialNo: "NOPE"));

        Assert.Contains("NOPE", ex.Message, StringComparison.Ordinal);
        Assert.Empty(await db.StockLedgerEntries.ToListAsync());
    }

    [Fact]
    public async Task A_serialised_layer_of_any_size_but_one_is_refused_by_the_domain()
    {
        var db = TestAppDbContext.Create();
        var service = new StockLedgerService(db);

        // The invariant that makes "a serial is a layer of quantity one" true rather than a
        // convention every caller has to remember.
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.IncrementAsync(
            OrganizationId, ProductId, WarehouseId, PrimaryQuantity.AlreadyPrimary(2m), 100m, DocumentType.PurchaseBill, Guid.NewGuid(),
            Day1, CancellationToken.None, serialNo: "A1"));
    }

    [Fact]
    public async Task An_issued_serial_reads_as_issued_and_an_in_stock_one_as_in_stock()
    {
        var db = TestAppDbContext.Create();
        var service = new StockLedgerService(db);

        await service.IncrementAsync(
            OrganizationId, ProductId, WarehouseId, PrimaryQuantity.AlreadyPrimary(1m), 100m, DocumentType.PurchaseBill, Guid.NewGuid(),
            Day1, CancellationToken.None, serialNo: "A1");
        await service.IncrementAsync(
            OrganizationId, ProductId, WarehouseId, PrimaryQuantity.AlreadyPrimary(1m), 100m, DocumentType.PurchaseBill, Guid.NewGuid(),
            Day1, CancellationToken.None, serialNo: "A2");
        await db.SaveChangesAsync(CancellationToken.None);

        await service.ConsumeAsync(
            OrganizationId, ProductId, WarehouseId, PrimaryQuantity.AlreadyPrimary(1m), DocumentType.Invoice, Guid.NewGuid(),
            Day2, CancellationToken.None, serialNo: "A1");
        await db.SaveChangesAsync(CancellationToken.None);

        // The Status filter the report catalogue shows, and which the product tab's three columns do
        // not explain, is exactly this column. Nothing was invented for it.
        var layers = await db.StockLedgerEntries.ToListAsync();
        Assert.Equal(0m, layers.Single(x => x.SerialNo == "A1").QuantityRemaining);
        Assert.Equal(1m, layers.Single(x => x.SerialNo == "A2").QuantityRemaining);
    }

    // ------------------------------------------------------------------ transfer and reversal

    [Fact]
    public async Task A_reversal_restocks_the_layers_own_batch_and_serial_not_a_fresh_argument()
    {
        var db = TestAppDbContext.Create();
        var service = new StockLedgerService(db);

        var billId = Guid.NewGuid();
        await service.IncrementAsync(
            OrganizationId, ProductId, WarehouseId, PrimaryQuantity.AlreadyPrimary(1m), 100m, DocumentType.PurchaseBill, billId,
            Day1, CancellationToken.None, batchId: BatchA, serialNo: "A1");
        await db.SaveChangesAsync(CancellationToken.None);

        await service.ReverseIncrementAsync(
            OrganizationId, DocumentType.PurchaseBill, billId, Day2, CancellationToken.None);
        await db.SaveChangesAsync(CancellationToken.None);

        // Phase 43's failure in this phase's shape: a release that landed in a different batch would
        // leave that batch permanently wrong while the product-wide total still reconciled.
        var reversal = await db.StockMovements
            .SingleAsync(x => x.Direction == StockMovementDirection.Out && x.SourceDocumentId == billId);

        Assert.Equal(BatchA, reversal.BatchId);
        Assert.Equal("A1", reversal.SerialNo);
    }

    [Fact]
    public async Task A_transfer_carries_each_batch_across_rather_than_averaging_them_away()
    {
        var db = TestAppDbContext.Create();
        var service = new StockLedgerService(db);

        await service.IncrementAsync(
            OrganizationId, ProductId, WarehouseId, PrimaryQuantity.AlreadyPrimary(10m), 5m, DocumentType.PurchaseBill, Guid.NewGuid(),
            Day1, CancellationToken.None, batchId: BatchA);
        await service.IncrementAsync(
            OrganizationId, ProductId, WarehouseId, PrimaryQuantity.AlreadyPrimary(10m), 8m, DocumentType.PurchaseBill, Guid.NewGuid(),
            Day2, CancellationToken.None, batchId: BatchB);
        await db.SaveChangesAsync(CancellationToken.None);

        var transferId = Guid.NewGuid();
        var consumption = await service.ConsumeAsync(
            OrganizationId, ProductId, WarehouseId, PrimaryQuantity.AlreadyPrimary(14m), DocumentType.WarehouseTransfer, transferId,
            Day3, CancellationToken.None);

        // What ApproveWarehouseTransferCommandHandler now does, relief by relief.
        foreach (var relief in consumption.Reliefs.Where(x => !x.Shortfall))
        {
            await service.IncrementAsync(
                OrganizationId, ProductId, OtherWarehouseId, PrimaryQuantity.AlreadyPrimary(relief.Quantity), relief.UnitCost,
                DocumentType.WarehouseTransfer, transferId, Day3, CancellationToken.None,
                batchId: relief.BatchId, serialNo: relief.SerialNo);
        }

        await db.SaveChangesAsync(CancellationToken.None);

        var destination = await db.StockLedgerEntries
            .Where(x => x.WarehouseId == OtherWarehouseId)
            .ToListAsync();

        // Two layers, not one averaged layer: batch identity AND the two distinct costs survive.
        Assert.Equal(2, destination.Count);
        Assert.Equal(10m, destination.Single(x => x.BatchId == BatchA).QuantityIn);
        Assert.Equal(5m, destination.Single(x => x.BatchId == BatchA).UnitCost);
        Assert.Equal(4m, destination.Single(x => x.BatchId == BatchB).QuantityIn);
        Assert.Equal(8m, destination.Single(x => x.BatchId == BatchB).UnitCost);

        // The conservation law across the move: nothing created, nothing destroyed.
        var all = await db.StockLedgerEntries.ToListAsync();
        Assert.Equal(20m, all.Sum(x => x.QuantityRemaining));
        Assert.Equal(10m * 5m + 10m * 8m, all.Sum(x => x.QuantityRemaining * x.UnitCost));
    }

    [Fact]
    public async Task A_products_on_hand_equals_the_sum_over_its_batches_plus_its_unbatched_layers()
    {
        var db = TestAppDbContext.Create();
        var service = new StockLedgerService(db);

        // The conservation law itself, stated as a property rather than as an arithmetic example:
        // the batch is a GROUP BY over the one quantity, so grouping can never lose or invent any.
        await service.IncrementAsync(
            OrganizationId, ProductId, WarehouseId, PrimaryQuantity.AlreadyPrimary(10m), 5m, DocumentType.PurchaseBill, Guid.NewGuid(),
            Day1, CancellationToken.None, batchId: BatchA);
        await service.IncrementAsync(
            OrganizationId, ProductId, WarehouseId, PrimaryQuantity.AlreadyPrimary(7m), 8m, DocumentType.PurchaseBill, Guid.NewGuid(),
            Day2, CancellationToken.None, batchId: BatchB);
        await db.SaveChangesAsync(CancellationToken.None);

        // ... an oversell against no batch, leaving an un-batched negative layer ...
        await service.ConsumeAsync(
            OrganizationId, ProductId, WarehouseId, PrimaryQuantity.AlreadyPrimary(20m), DocumentType.Invoice, Guid.NewGuid(),
            Day3, CancellationToken.None, allowNegative: true);
        await db.SaveChangesAsync(CancellationToken.None);

        var onHand = await service.GetAvailableQuantityAsync(
            OrganizationId, ProductId, WarehouseId, CancellationToken.None);

        var batchA = await service.GetAvailableQuantityAsync(
            OrganizationId, ProductId, WarehouseId, CancellationToken.None, BatchA);
        var batchB = await service.GetAvailableQuantityAsync(
            OrganizationId, ProductId, WarehouseId, CancellationToken.None, BatchB);

        var unbatched = await db.StockLedgerEntries
            .Where(x => x.OrganizationId == OrganizationId && x.ProductId == ProductId
                && x.WarehouseId == WarehouseId && x.BatchId == null)
            .SumAsync(x => x.QuantityRemaining);

        Assert.Equal(onHand, batchA + batchB + unbatched);
        Assert.Equal(-3m, onHand);
    }
}
