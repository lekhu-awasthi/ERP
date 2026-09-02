using ErpApp.Application.Inventory.Stock;
using ErpApp.Application.UnitTests.TestSupport;
using ErpApp.Domain.Catalog;
using ErpApp.Domain.Common;
using ErpApp.Domain.Inventory;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.UnitTests.Catalog;

/// <summary>
/// The roadmap's exit criterion, asserted at the FIFO engine itself: "a PurchaseBill/Invoice cycle
/// on one specific variant moves only that variant's stock".
///
/// Under Decision A (a variant IS a Product) this property is *structural* rather than
/// conditional -- two variants are two ProductIds, so the engine cannot confuse them without also
/// confusing two ordinary products. That is the whole argument for Decision A, and it is exactly
/// why these tests are worth writing anyway: they pin the property down so a future phase that
/// reintroduces a composite stock key has to keep it.
/// </summary>
public class VariantStockIsolationTests
{
    private static readonly Guid OrganizationId = Guid.NewGuid();
    private static readonly Guid WarehouseId = Guid.NewGuid();
    private static readonly Guid ColorId = Guid.NewGuid();
    private static readonly Guid SizeId = Guid.NewGuid();
    private static readonly Guid Blue = Guid.NewGuid();
    private static readonly Guid Red = Guid.NewGuid();
    private static readonly Guid Large = Guid.NewGuid();

    /// <summary>Builds a parent offering Large x {Blue, Red} plus its two variants.</summary>
    private static (Product Parent, Product LargeBlue, Product LargeRed) BuildMatrix()
    {
        var parent = Product.Create(
            OrganizationId, ProductType.Goods, "T-Shirt", "P-0001", Guid.NewGuid(), Guid.NewGuid(), null,
            true, 500m, 300m, VatRate.ThirteenPercentVat, 0, true);

        parent.SetVariantAttributeUsages([(SizeId, Large), (ColorId, Blue), (ColorId, Red)]);

        var largeBlue = parent.CreateVariant(
            "P-0002", "T-Shirt Large Blue", [(SizeId, Large), (ColorId, Blue)], 500m, 300m, null, null);
        var largeRed = parent.CreateVariant(
            "P-0003", "T-Shirt Large Red", [(SizeId, Large), (ColorId, Red)], 500m, 300m, null, null);

        return (parent, largeBlue, largeRed);
    }

    [Fact]
    public async Task Receiving_ten_of_one_variant_and_issuing_four_leaves_six_and_moves_no_sibling()
    {
        var db = TestAppDbContext.Create();
        var service = new StockLedgerService(db);
        var (parent, largeBlue, largeRed) = BuildMatrix();
        db.Products.AddRange(parent, largeBlue, largeRed);
        await db.SaveChangesAsync();

        // A PurchaseBill of 10 Large-Blue ...
        await service.IncrementAsync(
            OrganizationId, largeBlue.Id, WarehouseId, 10m, 300m, DocumentType.PurchaseBill, Guid.NewGuid(),
            new DateOnly(2026, 1, 1), CancellationToken.None);

        // ... and, so the sibling is genuinely in play, a PurchaseBill of 7 Large-Red.
        await service.IncrementAsync(
            OrganizationId, largeRed.Id, WarehouseId, 7m, 310m, DocumentType.PurchaseBill, Guid.NewGuid(),
            new DateOnly(2026, 1, 1), CancellationToken.None);
        await db.SaveChangesAsync();

        // An Invoice of 4 Large-Blue.
        await service.ConsumeAsync(
            OrganizationId, largeBlue.Id, WarehouseId, 4m, DocumentType.Invoice, Guid.NewGuid(),
            new DateOnly(2026, 1, 5), CancellationToken.None);
        await db.SaveChangesAsync();

        var blueOnHand = await service.GetAvailableQuantityAsync(
            OrganizationId, largeBlue.Id, WarehouseId, CancellationToken.None);
        var redOnHand = await service.GetAvailableQuantityAsync(
            OrganizationId, largeRed.Id, WarehouseId, CancellationToken.None);
        var parentOnHand = await service.GetAvailableQuantityAsync(
            OrganizationId, parent.Id, WarehouseId, CancellationToken.None);

        Assert.Equal(6m, blueOnHand);

        // Zero movement on the sibling -- the assertion the roadmap's exit criterion names.
        Assert.Equal(7m, redOnHand);

        // And the parent holds nothing at all: it is not transactable, so nothing can ever land on
        // it. A non-zero balance here would be the "40 shirts vs 12 Large-Blue" failure.
        Assert.Equal(0m, parentOnHand);
    }

    [Fact]
    public async Task The_kardex_reconciles_per_variant()
    {
        var db = TestAppDbContext.Create();
        var service = new StockLedgerService(db);
        var (parent, largeBlue, largeRed) = BuildMatrix();
        db.Products.AddRange(parent, largeBlue, largeRed);
        await db.SaveChangesAsync();

        await service.IncrementAsync(
            OrganizationId, largeBlue.Id, WarehouseId, 10m, 300m, DocumentType.PurchaseBill, Guid.NewGuid(),
            new DateOnly(2026, 1, 1), CancellationToken.None);
        await service.IncrementAsync(
            OrganizationId, largeRed.Id, WarehouseId, 7m, 310m, DocumentType.PurchaseBill, Guid.NewGuid(),
            new DateOnly(2026, 1, 1), CancellationToken.None);
        await db.SaveChangesAsync();

        await service.ConsumeAsync(
            OrganizationId, largeBlue.Id, WarehouseId, 4m, DocumentType.Invoice, Guid.NewGuid(),
            new DateOnly(2026, 1, 5), CancellationToken.None);
        await db.SaveChangesAsync();

        var blueMovements = await db.StockMovements.Where(x => x.ProductId == largeBlue.Id).ToListAsync();
        var redMovements = await db.StockMovements.Where(x => x.ProductId == largeRed.Id).ToListAsync();

        var blueNet = blueMovements.Sum(x => x.Direction == StockMovementDirection.In ? x.Quantity : -x.Quantity);
        var redNet = redMovements.Sum(x => x.Direction == StockMovementDirection.In ? x.Quantity : -x.Quantity);

        Assert.Equal(6m, blueNet);
        Assert.Equal(7m, redNet);
        Assert.Equal(2, blueMovements.Count);
        Assert.Single(redMovements);
        Assert.Empty(await db.StockMovements.Where(x => x.ProductId == parent.Id).ToListAsync());
    }

    [Fact]
    public async Task Fifo_costs_the_issue_from_that_variants_own_layers_only()
    {
        // Quantity-only assertions pass under implementations that cost wrongly, so this asserts
        // the weighted average AND that the sibling's cheaper layers were not walked to get it.
        var db = TestAppDbContext.Create();
        var service = new StockLedgerService(db);
        var (parent, largeBlue, largeRed) = BuildMatrix();
        db.Products.AddRange(parent, largeBlue, largeRed);
        await db.SaveChangesAsync();

        // Two receipts of Large-Blue at different costs.
        await service.IncrementAsync(
            OrganizationId, largeBlue.Id, WarehouseId, 10m, 100m, DocumentType.PurchaseBill, Guid.NewGuid(),
            new DateOnly(2026, 1, 1), CancellationToken.None);
        await service.IncrementAsync(
            OrganizationId, largeBlue.Id, WarehouseId, 10m, 200m, DocumentType.PurchaseBill, Guid.NewGuid(),
            new DateOnly(2026, 1, 10), CancellationToken.None);

        // A far cheaper sibling layer, older than both, that a product-keyed engine would walk first.
        await service.IncrementAsync(
            OrganizationId, largeRed.Id, WarehouseId, 100m, 1m, DocumentType.PurchaseBill, Guid.NewGuid(),
            new DateOnly(2025, 1, 1), CancellationToken.None);
        await db.SaveChangesAsync();

        // Issue 15 Large-Blue: 10 @ 100 + 5 @ 200 = 2000 over 15 = 133.333...
        var averageCost = await service.ConsumeAsync(
            OrganizationId, largeBlue.Id, WarehouseId, 15m, DocumentType.Invoice, Guid.NewGuid(),
            new DateOnly(2026, 2, 1), CancellationToken.None);
        await db.SaveChangesAsync();

        Assert.Equal(2000m / 15m, averageCost);

        // The sibling's layer is untouched -- had it been walked, the average would have collapsed
        // toward 1 and this would still have "balanced" on quantity.
        var redLayer = await db.StockLedgerEntries.SingleAsync(x => x.ProductId == largeRed.Id);
        Assert.Equal(100m, redLayer.QuantityRemaining);
        Assert.Equal(redLayer.QuantityIn, redLayer.QuantityRemaining);
    }

    [Fact]
    public async Task A_variant_cannot_be_issued_against_its_siblings_stock()
    {
        var db = TestAppDbContext.Create();
        var service = new StockLedgerService(db);
        var (parent, largeBlue, largeRed) = BuildMatrix();
        db.Products.AddRange(parent, largeBlue, largeRed);
        await db.SaveChangesAsync();

        await service.IncrementAsync(
            OrganizationId, largeRed.Id, WarehouseId, 50m, 10m, DocumentType.PurchaseBill, Guid.NewGuid(),
            new DateOnly(2026, 1, 1), CancellationToken.None);
        await db.SaveChangesAsync();

        await Assert.ThrowsAsync<Application.Common.Exceptions.ConflictException>(
            () => service.ConsumeAsync(
                OrganizationId, largeBlue.Id, WarehouseId, 1m, DocumentType.Invoice, Guid.NewGuid(),
                new DateOnly(2026, 1, 5), CancellationToken.None));
    }
}
