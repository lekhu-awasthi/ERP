using ErpApp.Application.Common.Exceptions;
using ErpApp.Application.Inventory.Stock;
using ErpApp.Application.UnitTests.TestSupport;
using ErpApp.Domain.Common;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.UnitTests.Inventory;

/// <summary>
/// The FIFO consumption engine is the highest-value test coverage in Phase 7 (roadmap's own
/// framing) -- these tests exercise IStockLedgerService directly against a fresh InMemory
/// IAppDbContext per test, no MediatR handler involved, since the service is a plain injectable
/// dependency (mirrors IGlPostingRule/IDocumentNumberGenerator's own test style).
/// </summary>
public class StockLedgerServiceTests
{
    private static readonly Guid OrganizationId = Guid.NewGuid();
    private static readonly Guid ProductId = Guid.NewGuid();
    private static readonly Guid WarehouseId = Guid.NewGuid();
    private static readonly Guid SourceDocumentId = Guid.NewGuid();

    [Fact]
    public async Task Consume_exact_layer_match_zeroes_the_layer_and_returns_its_unit_cost()
    {
        var db = TestAppDbContext.Create();
        var service = new StockLedgerService(db);

        await service.IncrementAsync(
            OrganizationId, ProductId, WarehouseId, 10m, 5m, DocumentType.PurchaseBill, SourceDocumentId,
            new DateOnly(2026, 1, 1), CancellationToken.None);
        await db.SaveChangesAsync(CancellationToken.None);

        var averageCost = await service.ConsumeAsync(
            OrganizationId, ProductId, WarehouseId, 10m, DocumentType.Invoice, Guid.NewGuid(),
            new DateOnly(2026, 1, 5), CancellationToken.None);
        await db.SaveChangesAsync(CancellationToken.None);

        Assert.Equal(5m, averageCost);
        var layer = await db.StockLedgerEntries.SingleAsync();
        Assert.Equal(0m, layer.QuantityRemaining);
    }

    [Fact]
    public async Task Consume_partial_layer_leaves_the_remainder_on_the_same_layer()
    {
        var db = TestAppDbContext.Create();
        var service = new StockLedgerService(db);

        await service.IncrementAsync(
            OrganizationId, ProductId, WarehouseId, 10m, 5m, DocumentType.PurchaseBill, SourceDocumentId,
            new DateOnly(2026, 1, 1), CancellationToken.None);
        await db.SaveChangesAsync(CancellationToken.None);

        var averageCost = await service.ConsumeAsync(
            OrganizationId, ProductId, WarehouseId, 4m, DocumentType.Invoice, Guid.NewGuid(),
            new DateOnly(2026, 1, 5), CancellationToken.None);
        await db.SaveChangesAsync(CancellationToken.None);

        Assert.Equal(5m, averageCost);
        var layer = await db.StockLedgerEntries.SingleAsync();
        Assert.Equal(6m, layer.QuantityRemaining);
        Assert.Equal(10m, layer.QuantityIn);
    }

    [Fact]
    public async Task Consume_spanning_multiple_layers_returns_the_correct_weighted_average_cost()
    {
        var db = TestAppDbContext.Create();
        var service = new StockLedgerService(db);

        // Older layer first: 5 units @ 10, then 5 units @ 20.
        await service.IncrementAsync(
            OrganizationId, ProductId, WarehouseId, 5m, 10m, DocumentType.PurchaseBill, Guid.NewGuid(),
            new DateOnly(2026, 1, 1), CancellationToken.None);
        await service.IncrementAsync(
            OrganizationId, ProductId, WarehouseId, 5m, 20m, DocumentType.PurchaseBill, Guid.NewGuid(),
            new DateOnly(2026, 1, 10), CancellationToken.None);
        await db.SaveChangesAsync(CancellationToken.None);

        // Consuming 8 should take all 5 of the older (cheaper) layer, then 3 of the newer one:
        // (5*10 + 3*20) / 8 = 13.75.
        var averageCost = await service.ConsumeAsync(
            OrganizationId, ProductId, WarehouseId, 8m, DocumentType.Invoice, Guid.NewGuid(),
            new DateOnly(2026, 1, 15), CancellationToken.None);
        await db.SaveChangesAsync(CancellationToken.None);

        Assert.Equal(13.75m, averageCost);

        var layers = await db.StockLedgerEntries.OrderBy(x => x.TransactionDate).ToListAsync();
        Assert.Equal(0m, layers[0].QuantityRemaining);
        Assert.Equal(2m, layers[1].QuantityRemaining);
    }

    [Fact]
    public async Task Consume_orders_by_transaction_date_not_by_insertion_order()
    {
        var db = TestAppDbContext.Create();
        var service = new StockLedgerService(db);

        // Insert the newer-dated layer first, older-dated layer second -- FIFO must still consume
        // the older-dated layer first regardless of row-creation order.
        await service.IncrementAsync(
            OrganizationId, ProductId, WarehouseId, 5m, 99m, DocumentType.PurchaseBill, Guid.NewGuid(),
            new DateOnly(2026, 2, 1), CancellationToken.None);
        await service.IncrementAsync(
            OrganizationId, ProductId, WarehouseId, 5m, 1m, DocumentType.PurchaseBill, Guid.NewGuid(),
            new DateOnly(2026, 1, 1), CancellationToken.None);
        await db.SaveChangesAsync(CancellationToken.None);

        var averageCost = await service.ConsumeAsync(
            OrganizationId, ProductId, WarehouseId, 5m, DocumentType.Invoice, Guid.NewGuid(),
            new DateOnly(2026, 3, 1), CancellationToken.None);
        await db.SaveChangesAsync(CancellationToken.None);

        Assert.Equal(1m, averageCost);
    }

    [Fact]
    public async Task Consume_more_than_available_throws_and_does_not_go_negative()
    {
        var db = TestAppDbContext.Create();
        var service = new StockLedgerService(db);

        await service.IncrementAsync(
            OrganizationId, ProductId, WarehouseId, 5m, 10m, DocumentType.PurchaseBill, SourceDocumentId,
            new DateOnly(2026, 1, 1), CancellationToken.None);
        await db.SaveChangesAsync(CancellationToken.None);

        await Assert.ThrowsAsync<ConflictException>(() => service.ConsumeAsync(
            OrganizationId, ProductId, WarehouseId, 6m, DocumentType.Invoice, Guid.NewGuid(),
            new DateOnly(2026, 1, 5), CancellationToken.None));

        var layer = await db.StockLedgerEntries.SingleAsync();
        Assert.Equal(5m, layer.QuantityRemaining);
    }

    [Fact]
    public async Task Consume_zero_quantity_is_a_noop()
    {
        var db = TestAppDbContext.Create();
        var service = new StockLedgerService(db);

        await service.IncrementAsync(
            OrganizationId, ProductId, WarehouseId, 5m, 10m, DocumentType.PurchaseBill, SourceDocumentId,
            new DateOnly(2026, 1, 1), CancellationToken.None);
        await db.SaveChangesAsync(CancellationToken.None);

        var averageCost = await service.ConsumeAsync(
            OrganizationId, ProductId, WarehouseId, 0m, DocumentType.Invoice, Guid.NewGuid(),
            new DateOnly(2026, 1, 5), CancellationToken.None);

        Assert.Equal(0m, averageCost);
        var layer = await db.StockLedgerEntries.SingleAsync();
        Assert.Equal(5m, layer.QuantityRemaining);
    }

    [Fact]
    public async Task Consume_with_no_layers_at_all_throws()
    {
        var db = TestAppDbContext.Create();
        var service = new StockLedgerService(db);

        await Assert.ThrowsAsync<ConflictException>(() => service.ConsumeAsync(
            OrganizationId, ProductId, WarehouseId, 1m, DocumentType.Invoice, Guid.NewGuid(),
            new DateOnly(2026, 1, 5), CancellationToken.None));
    }

    [Fact]
    public async Task Increment_zero_quantity_is_a_noop()
    {
        var db = TestAppDbContext.Create();
        var service = new StockLedgerService(db);

        await service.IncrementAsync(
            OrganizationId, ProductId, WarehouseId, 0m, 10m, DocumentType.PurchaseBill, SourceDocumentId,
            new DateOnly(2026, 1, 1), CancellationToken.None);
        await db.SaveChangesAsync(CancellationToken.None);

        Assert.Empty(db.StockLedgerEntries);
    }

    [Fact]
    public async Task GetAvailableQuantityAsync_sums_across_layers_and_scopes_by_product_and_warehouse()
    {
        var db = TestAppDbContext.Create();
        var service = new StockLedgerService(db);
        var otherWarehouseId = Guid.NewGuid();

        await service.IncrementAsync(
            OrganizationId, ProductId, WarehouseId, 5m, 10m, DocumentType.PurchaseBill, Guid.NewGuid(),
            new DateOnly(2026, 1, 1), CancellationToken.None);
        await service.IncrementAsync(
            OrganizationId, ProductId, WarehouseId, 3m, 12m, DocumentType.PurchaseBill, Guid.NewGuid(),
            new DateOnly(2026, 1, 2), CancellationToken.None);
        // A layer in a different warehouse must not count toward this warehouse's availability.
        await service.IncrementAsync(
            OrganizationId, ProductId, otherWarehouseId, 100m, 1m, DocumentType.PurchaseBill, Guid.NewGuid(),
            new DateOnly(2026, 1, 1), CancellationToken.None);
        await db.SaveChangesAsync(CancellationToken.None);

        var available = await service.GetAvailableQuantityAsync(OrganizationId, ProductId, WarehouseId, CancellationToken.None);

        Assert.Equal(8m, available);
    }

    [Fact]
    public async Task GetAvailableQuantityAsync_with_no_layers_returns_zero()
    {
        var db = TestAppDbContext.Create();
        var service = new StockLedgerService(db);

        var available = await service.GetAvailableQuantityAsync(OrganizationId, ProductId, WarehouseId, CancellationToken.None);

        Assert.Equal(0m, available);
    }

    [Fact]
    public async Task PreviewConsumptionCostAsync_does_not_mutate_layers()
    {
        var db = TestAppDbContext.Create();
        var service = new StockLedgerService(db);

        await service.IncrementAsync(
            OrganizationId, ProductId, WarehouseId, 5m, 10m, DocumentType.PurchaseBill, SourceDocumentId,
            new DateOnly(2026, 1, 1), CancellationToken.None);
        await db.SaveChangesAsync(CancellationToken.None);

        var estimate = await service.PreviewConsumptionCostAsync(OrganizationId, ProductId, WarehouseId, 3m, CancellationToken.None);
        await db.SaveChangesAsync(CancellationToken.None);

        Assert.Equal(10m, estimate);
        var layer = await db.StockLedgerEntries.SingleAsync();
        Assert.Equal(5m, layer.QuantityRemaining);
    }

    [Fact]
    public async Task PreviewConsumptionCostAsync_caps_at_available_quantity_instead_of_throwing()
    {
        var db = TestAppDbContext.Create();
        var service = new StockLedgerService(db);

        await service.IncrementAsync(
            OrganizationId, ProductId, WarehouseId, 2m, 10m, DocumentType.PurchaseBill, SourceDocumentId,
            new DateOnly(2026, 1, 1), CancellationToken.None);
        await db.SaveChangesAsync(CancellationToken.None);

        var estimate = await service.PreviewConsumptionCostAsync(OrganizationId, ProductId, WarehouseId, 10m, CancellationToken.None);

        Assert.Equal(10m, estimate);
    }

    [Fact]
    public async Task PreviewConsumptionCostAsync_with_no_layers_returns_zero()
    {
        var db = TestAppDbContext.Create();
        var service = new StockLedgerService(db);

        var estimate = await service.PreviewConsumptionCostAsync(OrganizationId, ProductId, WarehouseId, 5m, CancellationToken.None);

        Assert.Equal(0m, estimate);
    }

    /// <summary>Void lifecycle (roadmap Phase 16a) -- an untouched layer created by a document
    /// being voided is fully zeroed out (QuantityIn is left alone, same invariant Consume already
    /// keeps, so kardex history stays reconstructable).</summary>
    [Fact]
    public async Task ReverseIncrementAsync_on_a_fully_intact_layer_zeroes_it()
    {
        var db = TestAppDbContext.Create();
        var service = new StockLedgerService(db);

        await service.IncrementAsync(
            OrganizationId, ProductId, WarehouseId, 10m, 5m, DocumentType.PurchaseBill, SourceDocumentId,
            new DateOnly(2026, 1, 1), CancellationToken.None);
        await db.SaveChangesAsync(CancellationToken.None);

        await service.ReverseIncrementAsync(
            OrganizationId, DocumentType.PurchaseBill, SourceDocumentId, new DateOnly(2026, 1, 10), CancellationToken.None);
        await db.SaveChangesAsync(CancellationToken.None);

        var layer = await db.StockLedgerEntries.SingleAsync();
        Assert.Equal(0m, layer.QuantityRemaining);
        Assert.Equal(10m, layer.QuantityIn);
    }

    /// <summary>The roadmap's explicit "a partly-consumed PurchaseBill must be REJECTED, not
    /// partially unwound" requirement -- confirmed the guard checks every layer for this document
    /// before mutating any of them.</summary>
    [Fact]
    public async Task ReverseIncrementAsync_throws_and_does_not_mutate_when_layer_is_partly_consumed()
    {
        var db = TestAppDbContext.Create();
        var service = new StockLedgerService(db);

        await service.IncrementAsync(
            OrganizationId, ProductId, WarehouseId, 10m, 5m, DocumentType.PurchaseBill, SourceDocumentId,
            new DateOnly(2026, 1, 1), CancellationToken.None);
        await db.SaveChangesAsync(CancellationToken.None);
        await service.ConsumeAsync(
            OrganizationId, ProductId, WarehouseId, 4m, DocumentType.Invoice, Guid.NewGuid(), new DateOnly(2026, 1, 5),
            CancellationToken.None);
        await db.SaveChangesAsync(CancellationToken.None);

        await Assert.ThrowsAsync<ConflictException>(() => service.ReverseIncrementAsync(
            OrganizationId, DocumentType.PurchaseBill, SourceDocumentId, new DateOnly(2026, 1, 10), CancellationToken.None));

        var layer = await db.StockLedgerEntries.SingleAsync();
        Assert.Equal(6m, layer.QuantityRemaining);
    }

    [Fact]
    public async Task ReverseIncrementAsync_with_no_matching_layers_is_a_noop()
    {
        var db = TestAppDbContext.Create();
        var service = new StockLedgerService(db);

        await service.ReverseIncrementAsync(
            OrganizationId, DocumentType.PurchaseBill, Guid.NewGuid(), new DateOnly(2026, 1, 10), CancellationToken.None);

        Assert.Empty(db.StockLedgerEntries);
    }
}
