using ErpApp.Application.Common.Persistence;
using ErpApp.Application.Inventory;
using ErpApp.Application.Inventory.Commands.ApproveInventoryAdjustment;
using ErpApp.Application.Inventory.Commands.CreateInventoryAdjustment;
using ErpApp.Application.Inventory.Commands.UpdateInventoryAdjustment;
using ErpApp.Application.Inventory.Commands.VoidInventoryAdjustment;
using ErpApp.Application.Inventory.Posting;
using ErpApp.Application.Inventory.Queries.GetInventoryAdjustment;
using ErpApp.Application.Inventory.Stock;
using ErpApp.Application.UnitTests.TestSupport;
using ErpApp.Domain.Catalog;
using ErpApp.Domain.Inventory;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace ErpApp.Application.UnitTests.Inventory;

/// <summary>
/// Phase 54 — Inventory Adjustment, the one of phase 52's four deferred line types that turned out
/// to carry a unit.
///
/// <para><b>Why these tests exist rather than a line in the sweep guard.</b> The guard asserts that
/// the columns are present and that the request record accepts a unit; it cannot see whether the
/// <i>handlers</i> use them, which is exactly the blindness phase 44 found in two statutory
/// registers that had narrowed only their return half for three phases. The three things worth
/// pinning here are all behavioural: what reaches the ledger, what reaches the GL, and what comes
/// back on a Void.</para>
///
/// <para><b>The arithmetic under test</b> is phase 52's law applied to a cost-bearing inventory
/// line: a unit converts the <b>quantity</b> and never the money. An Increase line for
/// <c>2 CT @ 1,200</c> on a product whose Carton is 12 Pieces is an Amount of 2,400 — not 28,800 —
/// and it creates <b>24</b> primary units at <b>100</b> each.</para>
/// </summary>
public sealed class InventoryAdjustmentUnitTests
{
    private static readonly DateOnly Date = new(2026, 6, 1);

    /// <summary>Carton = 12 Pieces, on the seed's first product.</summary>
    private static async Task<(InventoryReportSeed.Seed Seed, Guid CartonUnitId)> SeedWithCartonAsync(
        IAppDbContext db, decimal cartonRate = 12m)
    {
        var seed = await InventoryReportSeed.CreateAsync(db);

        var carton = UnitOfMeasurement.Create(seed.OrganizationId, "Carton", "CT");
        db.UnitsOfMeasurement.Add(carton);

        var product = await db.Products
            .Include(x => x.SecondaryUnits)
            .SingleAsync(x => x.Id == seed.ProductId, CancellationToken.None);
        // Through the child DbSet, not left to collection fixup: a child appended to an
        // already-tracked parent's encapsulated collection is detected as Modified rather than
        // Added, and InMemory answers with DbUpdateConcurrencyException (phase-24 bug #1). This is
        // the same call AddSecondaryUnitCommandHandler makes.
        db.ProductSecondaryUnits.Add(product.AddSecondaryUnit(carton.Id, cartonRate, 0m, 0m));

        await db.SaveChangesAsync(CancellationToken.None);

        return (seed, carton.Id);
    }

    private static Task<CreateInventoryAdjustmentResult> CreateAsync(
        IAppDbContext db, InventoryReportSeed.Seed seed, InventoryAdjustmentLineInput line) =>
        new CreateInventoryAdjustmentCommandHandler(db).Handle(
            new CreateInventoryAdjustmentCommand(seed.OrganizationId, seed.WarehouseId, Date, null, [line]),
            CancellationToken.None);

    private static Task<ApproveInventoryAdjustmentResult> ApproveAsync(
        IAppDbContext db, InventoryReportSeed.Seed seed, Guid id) =>
        new ApproveInventoryAdjustmentCommandHandler(
                db, seed.NumberGenerator, new FakeCurrentUserService(Guid.NewGuid()),
                new InventoryAdjustmentPostingRule(), new StockLedgerService(db))
            .Handle(new ApproveInventoryAdjustmentCommand(seed.OrganizationId, id), CancellationToken.None);

    private static Task<VoidInventoryAdjustmentResult> VoidAsync(
        IAppDbContext db, InventoryReportSeed.Seed seed, Guid id) =>
        new VoidInventoryAdjustmentCommandHandler(
                db, new FakeCurrentUserService(Guid.NewGuid()), new StockLedgerService(db))
            .Handle(new VoidInventoryAdjustmentCommand(seed.OrganizationId, id), CancellationToken.None);

    private static async Task<(decimal QuantityIn, decimal UnitCost)> TheOneLayerAsync(
        IAppDbContext db, InventoryReportSeed.Seed seed)
    {
        var layer = await db.StockLedgerEntries
            .Where(x => x.OrganizationId == seed.OrganizationId && x.ProductId == seed.ProductId)
            .SingleAsync(CancellationToken.None);

        return (layer.QuantityIn, layer.UnitCost);
    }

    /// <summary>
    /// <b>The unit law.</b> Two cartons at 1,200 is 24 pieces at 100 — and the Amount the GL sees
    /// is 2,400, the same figure a line entered as 24 pieces at 100 would produce. That the money
    /// is untouched is the half a factor-multiplying bug would get wrong while still balancing.
    /// </summary>
    [Fact]
    public async Task An_increase_entered_in_cartons_creates_primary_units_at_the_per_primary_cost()
    {
        var db = TestAppDbContext.Create();
        var (seed, cartonId) = await SeedWithCartonAsync(db);

        var created = await CreateAsync(
            db, seed,
            new InventoryAdjustmentLineInput(seed.ProductId, InventoryAdjustmentDirection.Increase, 2m, 1200m, cartonId));
        await ApproveAsync(db, seed, created.Id);

        var (quantityIn, unitCost) = await TheOneLayerAsync(db, seed);

        Assert.Equal(24m, quantityIn);
        Assert.Equal(100m, unitCost);

        // All three views of stock value, not any two of them (phase 37): the layers, the Inventory
        // account and the append-only movement history.
        await StockConservation.AssertHoldsAsync(db, seed.OrganizationId);
        var (layers, _, _) = await StockConservation.MeasureAsync(db, seed.OrganizationId);
        Assert.Equal(2400m, layers);
    }

    /// <summary>
    /// The same line entered in the primary unit reaches exactly the same ledger and the same GL.
    /// This is the control for the test above: without it, an arithmetic error that scaled both the
    /// quantity and the money would still look self-consistent.
    /// </summary>
    [Fact]
    public async Task The_same_stock_entered_in_pieces_produces_the_identical_ledger_and_GL()
    {
        var db = TestAppDbContext.Create();
        var (seed, _) = await SeedWithCartonAsync(db);

        var created = await CreateAsync(
            db, seed,
            new InventoryAdjustmentLineInput(seed.ProductId, InventoryAdjustmentDirection.Increase, 24m, 100m));
        await ApproveAsync(db, seed, created.Id);

        var (quantityIn, unitCost) = await TheOneLayerAsync(db, seed);

        Assert.Equal(24m, quantityIn);
        Assert.Equal(100m, unitCost);

        await StockConservation.AssertHoldsAsync(db, seed.OrganizationId);
        var (layers, _, _) = await StockConservation.MeasureAsync(db, seed.OrganizationId);
        Assert.Equal(2400m, layers);
    }

    /// <summary>
    /// A Decrease line consumes the <b>primary</b> quantity. One carton written off is twelve
    /// pieces gone, and the GL is credited what those twelve actually cost, not what one of
    /// anything cost.
    /// </summary>
    [Fact]
    public async Task A_decrease_entered_in_cartons_consumes_the_primary_quantity()
    {
        var db = TestAppDbContext.Create();
        var (seed, cartonId) = await SeedWithCartonAsync(db);

        await InventoryReportSeed.PurchaseAsync(db, seed, Date, 20m, 10m);

        var created = await CreateAsync(
            db, seed,
            new InventoryAdjustmentLineInput(seed.ProductId, InventoryAdjustmentDirection.Decrease, 1m, 0m, cartonId));
        await ApproveAsync(db, seed, created.Id);

        var layer = await db.StockLedgerEntries
            .Where(x => x.OrganizationId == seed.OrganizationId && x.ProductId == seed.ProductId)
            .SingleAsync(CancellationToken.None);

        Assert.Equal(8m, layer.QuantityRemaining);

        // ConsumedUnitCost is per primary unit -- 10, the cost of a piece, not 120, the cost of a
        // carton. The Void below depends on that being true.
        var line = await db.InventoryAdjustmentLines
            .SingleAsync(x => x.InventoryAdjustmentId == created.Id, CancellationToken.None);
        Assert.Equal(10m, line.ConsumedUnitCost);

        await StockConservation.AssertHoldsAsync(db, seed.OrganizationId);
    }

    /// <summary>
    /// <b>The bug the required parameters caught.</b> Voiding a Decrease line entered in cartons
    /// has to put back the twelve pieces that left, at the cost they left at. Before this phase the
    /// Void handler passed <c>PrimaryQuantity.AlreadyPrimary(line.Quantity)</c>, which would have
    /// restocked <i>one</i> piece — the same pair of bugs phase 52's distinct
    /// <c>PrimaryQuantity</c> type caught in <c>VoidInvoice</c> and <c>VoidDebitNote</c>. Nothing
    /// short of a behavioural assertion sees it: the document, the GL and the layer count all look
    /// right either way.
    /// </summary>
    [Fact]
    public async Task Voiding_a_decrease_restocks_the_primary_quantity_and_not_the_entered_one()
    {
        var db = TestAppDbContext.Create();
        var (seed, cartonId) = await SeedWithCartonAsync(db);

        await InventoryReportSeed.PurchaseAsync(db, seed, Date, 20m, 10m);
        var (before, _, _) = await StockConservation.MeasureAsync(db, seed.OrganizationId);

        var created = await CreateAsync(
            db, seed,
            new InventoryAdjustmentLineInput(seed.ProductId, InventoryAdjustmentDirection.Decrease, 1m, 0m, cartonId));
        await ApproveAsync(db, seed, created.Id);
        await VoidAsync(db, seed, created.Id);

        var onHand = await db.StockLedgerEntries
            .Where(x => x.OrganizationId == seed.OrganizationId && x.ProductId == seed.ProductId)
            .SumAsync(x => x.QuantityRemaining, CancellationToken.None);

        Assert.Equal(20m, onHand);

        var (after, _, _) = await StockConservation.MeasureAsync(db, seed.OrganizationId);
        Assert.Equal(before, after);
        await StockConservation.AssertHoldsAsync(db, seed.OrganizationId);
    }

    /// <summary>
    /// The freeze, the half a handler test can express: the factor is resolved once at Create, so
    /// changing the product's conversion rate afterwards does not move a line that already exists.
    /// (What a rate edit does to an <i>approved</i> document's ledger history needs the real
    /// database and is proven in the manual E2E — phase 52's `DocumentLineUnitTests` says the same
    /// about its own half.)
    /// </summary>
    [Fact]
    public async Task The_factor_is_frozen_at_create_and_a_later_rate_edit_does_not_move_it()
    {
        var db = TestAppDbContext.Create();
        var (seed, cartonId) = await SeedWithCartonAsync(db);

        var created = await CreateAsync(
            db, seed,
            new InventoryAdjustmentLineInput(seed.ProductId, InventoryAdjustmentDirection.Increase, 2m, 1200m, cartonId));

        var product = await db.Products
            .Include(x => x.SecondaryUnits)
            .SingleAsync(x => x.Id == seed.ProductId, CancellationToken.None);
        var row = product.SecondaryUnits.Single(x => x.UnitId == cartonId);
        product.UpdateSecondaryUnit(row.Id, 6m, 0m, 0m);
        await db.SaveChangesAsync(CancellationToken.None);

        await ApproveAsync(db, seed, created.Id);

        var (quantityIn, unitCost) = await TheOneLayerAsync(db, seed);

        Assert.Equal(24m, quantityIn);
        Assert.Equal(100m, unitCost);
    }

    /// <summary>
    /// An <b>edit</b> re-resolves, which is the other half of the same rule: a draft is still being
    /// written, so it takes the catalogue as it stands. Only Approve makes the answer history.
    /// </summary>
    [Fact]
    public async Task An_edit_re_resolves_the_factor_from_the_catalogue_as_it_now_stands()
    {
        var db = TestAppDbContext.Create();
        var (seed, cartonId) = await SeedWithCartonAsync(db);

        var created = await CreateAsync(
            db, seed,
            new InventoryAdjustmentLineInput(seed.ProductId, InventoryAdjustmentDirection.Increase, 2m, 1200m, cartonId));

        var product = await db.Products
            .Include(x => x.SecondaryUnits)
            .SingleAsync(x => x.Id == seed.ProductId, CancellationToken.None);
        var row = product.SecondaryUnits.Single(x => x.UnitId == cartonId);
        product.UpdateSecondaryUnit(row.Id, 6m, 0m, 0m);
        await db.SaveChangesAsync(CancellationToken.None);

        await new UpdateInventoryAdjustmentCommandHandler(db).Handle(
            new UpdateInventoryAdjustmentCommand(
                seed.OrganizationId, created.Id, seed.WarehouseId, Date, null,
                [new InventoryAdjustmentLineInput(seed.ProductId, InventoryAdjustmentDirection.Increase, 2m, 1200m, cartonId)]),
            CancellationToken.None);

        await ApproveAsync(db, seed, created.Id);

        var (quantityIn, unitCost) = await TheOneLayerAsync(db, seed);

        Assert.Equal(12m, quantityIn);
        Assert.Equal(200m, unitCost);
    }

    /// <summary>
    /// The read half. Phase 35a found 14 of 15 detail DTOs dropping a field the aggregate had,
    /// after which the form posted its own default over the stored value on every edit — so the
    /// detail query is asserted to carry the unit, its short name and the frozen factor.
    /// </summary>
    [Fact]
    public async Task The_detail_query_carries_the_unit_its_short_name_and_the_frozen_factor()
    {
        var db = TestAppDbContext.Create();
        var (seed, cartonId) = await SeedWithCartonAsync(db);

        var created = await CreateAsync(
            db, seed,
            new InventoryAdjustmentLineInput(seed.ProductId, InventoryAdjustmentDirection.Increase, 2m, 1200m, cartonId));

        var detail = await new GetInventoryAdjustmentQueryHandler(db).Handle(
            new GetInventoryAdjustmentQuery(seed.OrganizationId, created.Id), CancellationToken.None);

        var line = Assert.Single(detail.Lines);

        Assert.Equal(cartonId, line.UnitId);
        Assert.Equal("CT", line.UnitName);
        Assert.Equal(12m, line.ConversionFactor);
    }

    /// <summary>
    /// A unit the product does not have is a 400 naming the line, never a silent fallback to the
    /// primary — the guarantee the whole mechanism rests on, and the reason the client sends a unit
    /// id rather than a factor.
    /// </summary>
    [Fact]
    public async Task A_unit_the_product_does_not_have_is_a_validation_failure_naming_the_line()
    {
        var db = TestAppDbContext.Create();
        var (seed, _) = await SeedWithCartonAsync(db);

        var stranger = UnitOfMeasurement.Create(seed.OrganizationId, "Pallet", "PLT");
        db.UnitsOfMeasurement.Add(stranger);
        await db.SaveChangesAsync(CancellationToken.None);

        var failure = await Assert.ThrowsAsync<FluentValidation.ValidationException>(() =>
            CreateAsync(
                db, seed,
                new InventoryAdjustmentLineInput(
                    seed.ProductId, InventoryAdjustmentDirection.Increase, 2m, 1200m, stranger.Id)));

        Assert.Contains(failure.Errors, x => x.PropertyName == "Lines[0]");
    }
}
