using ErpApp.Application.Common.Exceptions;
using ErpApp.Application.Common.Persistence;
using ErpApp.Application.Inventory.Stock;
using ErpApp.Application.Manufacturing;
using ErpApp.Application.Manufacturing.Commands.ApproveProductionJournal;
using ErpApp.Application.Manufacturing.Commands.CreateProductionJournal;
using ErpApp.Application.Manufacturing.Posting;
using ErpApp.Application.Sales.Stock;
using ErpApp.Application.UnitTests.TestSupport;
using ErpApp.Domain.Common;
using ErpApp.Domain.Inventory;
using ErpApp.Domain.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.UnitTests.Manufacturing;

/// <summary>
/// <b>The exit criterion, at handler level.</b> Raw stock is consumed at real FIFO cost, finished
/// stock is created at the computed cost, the GL balances <i>and</i> nets correctly, and the kardex
/// reconciles. Everything here is seeded through real Create/Approve handlers, so the FIFO layers
/// under test were built by the engine that consumes them.
/// </summary>
public class ApproveProductionJournalCommandHandlerTests
{
    private static readonly DateOnly Day1 = new(2026, 1, 10);
    private static readonly DateOnly Day2 = new(2026, 1, 20);
    private static readonly DateOnly RunDay = new(2026, 1, 25);

    [Fact]
    public async Task Raw_material_cost_is_the_weighted_average_across_two_fifo_layers_not_the_latest_rate()
    {
        var db = TestAppDbContext.Create();
        var seed = await ManufacturingTestSeed.CreateAsync(db);

        // 10 @ 100 then 10 @ 200. Consuming 15 takes all of the first layer and 5 of the second:
        // (10*100 + 5*200) = 2000, a weighted average of 133.3333 -- not 200 (latest), not 150
        // (simple average), not 100 (oldest).
        await ManufacturingTestSeed.ReceiveStockAsync(db, seed, seed.RawProductId, 10m, 100m, Day1);
        await ManufacturingTestSeed.ReceiveStockAsync(db, seed, seed.RawProductId, 10m, 200m, Day2);

        var result = await CreateAndApproveAsync(db, seed, outputQuantity: 10m, rawQuantity: 15m);

        Assert.Equal(2000m, result.RawMaterialCost);
        Assert.Equal(200m, result.FinishedGoodsUnitCost);

        var line = await db.ProductionJournalRawMaterialLines.SingleAsync(x => x.ProductionJournalId == result.Id);
        Assert.Equal(2000m, line.Amount);
        Assert.Equal(133.3333m, Math.Round(line.ConsumedUnitCost!.Value, 4));
    }

    [Fact]
    public async Task The_conservation_law_holds_across_the_ledger_the_document_and_the_general_ledger()
    {
        var db = TestAppDbContext.Create();
        var seed = await ManufacturingTestSeed.CreateAsync(db);

        await ManufacturingTestSeed.ReceiveStockAsync(db, seed, seed.RawProductId, 100m, 60m, Day1);

        // 20 raw @ 60 = 1200 consumed, plus 300 of expenses = 1500 total; a by-product takes 20%
        // (300) leaving 1200 for 10 finished units.
        var result = await CreateAndApproveAsync(
            db, seed, outputQuantity: 10m, rawQuantity: 20m, expenseAmount: 300m, byProductPct: 20m, byProductQuantity: 6m);

        Assert.Equal(1200m, result.RawMaterialCost);
        Assert.Equal(300m, result.ProductionExpenseCost);
        Assert.Equal(1500m, result.TotalCostOfProduction);
        Assert.Equal(300m, result.CostAllocatedToByProduct);
        Assert.Equal(1200m, result.FinishedGoodsCost);
        Assert.Equal(120m, result.FinishedGoodsUnitCost);
        Assert.Equal(0m, result.CostRoundingAdjustment);

        // value in == value out, to the cent.
        Assert.Equal(
            result.RawMaterialCost + result.ProductionExpenseCost,
            result.FinishedGoodsCost + result.CostAllocatedToByProduct);

        // ...and the ledger agrees with the document.
        var layers = await db.StockLedgerEntries
            .Where(x => x.SourceDocumentType == DocumentType.ProductionJournal && x.SourceDocumentId == result.Id)
            .ToListAsync();

        var finishedLayer = layers.Single(x => x.ProductId == seed.FinishedProductId);
        Assert.Equal(10m, finishedLayer.QuantityIn);
        Assert.Equal(120m, finishedLayer.UnitCost);

        var byProductLayer = layers.Single(x => x.ProductId == seed.ByProductId);
        Assert.Equal(6m, byProductLayer.QuantityIn);
        Assert.Equal(50m, byProductLayer.UnitCost);

        Assert.Equal(1500m, layers.Sum(x => x.QuantityIn * x.UnitCost));

        // 100 received less 20 consumed leaves 80 on the raw material's own layer.
        var rawRemaining = await new StockLedgerService(db).GetAvailableQuantityAsync(
            seed.OrganizationId, seed.RawProductId, seed.WarehouseId, CancellationToken.None);
        Assert.Equal(80m, rawRemaining);
    }

    [Fact]
    public async Task The_general_ledger_balances_and_inventory_nets_to_exactly_the_production_expenses()
    {
        var db = TestAppDbContext.Create();
        var seed = await ManufacturingTestSeed.CreateAsync(db);

        await ManufacturingTestSeed.ReceiveStockAsync(db, seed, seed.RawProductId, 100m, 60m, Day1);

        var result = await CreateAndApproveAsync(
            db, seed, outputQuantity: 10m, rawQuantity: 20m, expenseAmount: 300m, byProductPct: 20m, byProductQuantity: 6m);

        var glLines = await ManufacturingTestSeed.GlLinesForAsync(
            db, seed.OrganizationId, DocumentType.ProductionJournal, result.Id);

        // Balanced, which GlJournalEntry.Post already guarantees...
        Assert.Equal(glLines.Sum(x => x.Debit), glLines.Sum(x => x.Credit));

        // ...but phase-6 bug #3 is about the NET per account, which balancing does not prove.
        // Inventory: 1200 finished + 300 by-product debited, 1200 raw credited = +300, exactly the
        // expenses capitalised into stock. Not zero, and not the full raw-material value.
        Assert.Equal(300m, ManufacturingTestSeed.NetMovement(glLines, seed.InventoryAccountId));

        // The other side is a credit of the same figure to the production cost account.
        Assert.Equal(-300m, ManufacturingTestSeed.NetMovement(glLines, seed.ProductionCostAccountId));

        // And nothing else was touched -- no WIP account, no COGS, no purchase expense.
        var touched = glLines.Select(x => x.AccountId).Distinct().ToHashSet();
        Assert.Equal(2, touched.Count);
        Assert.Contains(seed.InventoryAccountId, touched);
        Assert.Contains(seed.ProductionCostAccountId, touched);
    }

    [Fact]
    public async Task An_expense_free_run_nets_inventory_to_zero_and_posts_no_production_cost_line()
    {
        var db = TestAppDbContext.Create();
        var seed = await ManufacturingTestSeed.CreateAsync(db);

        await ManufacturingTestSeed.ReceiveStockAsync(db, seed, seed.RawProductId, 100m, 60m, Day1);

        var result = await CreateAndApproveAsync(db, seed, outputQuantity: 10m, rawQuantity: 20m);

        var glLines = await ManufacturingTestSeed.GlLinesForAsync(
            db, seed.OrganizationId, DocumentType.ProductionJournal, result.Id);

        // Raw material simply changed form: the asset value did not move at all.
        Assert.Equal(0m, ManufacturingTestSeed.NetMovement(glLines, seed.InventoryAccountId));
        Assert.DoesNotContain(glLines, x => x.AccountId == seed.ProductionCostAccountId);
        Assert.Equal(glLines.Sum(x => x.Debit), glLines.Sum(x => x.Credit));
    }

    [Fact]
    public async Task The_kardex_records_one_out_and_one_in_per_product()
    {
        var db = TestAppDbContext.Create();
        var seed = await ManufacturingTestSeed.CreateAsync(db);

        await ManufacturingTestSeed.ReceiveStockAsync(db, seed, seed.RawProductId, 100m, 60m, Day1);

        var result = await CreateAndApproveAsync(
            db, seed, outputQuantity: 10m, rawQuantity: 20m, expenseAmount: 300m, byProductPct: 20m, byProductQuantity: 6m);

        var movements = await db.StockMovements
            .Where(x => x.SourceDocumentType == DocumentType.ProductionJournal && x.SourceDocumentId == result.Id)
            .ToListAsync();

        Assert.Equal(3, movements.Count);

        var outbound = movements.Single(x => x.Direction == StockMovementDirection.Out);
        Assert.Equal(seed.RawProductId, outbound.ProductId);
        Assert.Equal(20m, outbound.Quantity);
        Assert.Equal(60m, outbound.UnitCost);

        var inbound = movements.Where(x => x.Direction == StockMovementDirection.In).ToList();
        Assert.Equal(1500m, inbound.Sum(x => x.Quantity * x.UnitCost));
    }

    [Fact]
    public async Task A_run_short_of_raw_stock_hits_the_tenants_reject_policy_rather_than_a_hardcoded_throw()
    {
        var db = TestAppDbContext.Create();
        var seed = await ManufacturingTestSeed.CreateAsync(db, BalanceAction.Reject);

        await ManufacturingTestSeed.ReceiveStockAsync(db, seed, seed.RawProductId, 5m, 60m, Day1);

        var error = await Assert.ThrowsAsync<ConflictException>(
            () => CreateAndApproveAsync(db, seed, outputQuantity: 10m, rawQuantity: 20m));

        Assert.Contains("raw-material stock", error.Message, StringComparison.Ordinal);

        // Nothing was consumed: the check runs before any mutation.
        var remaining = await new StockLedgerService(db).GetAvailableQuantityAsync(
            seed.OrganizationId, seed.RawProductId, seed.WarehouseId, CancellationToken.None);
        Assert.Equal(5m, remaining);
    }

    [Fact]
    public async Task A_warn_tenant_gets_a_confirmable_warning_first_and_proceeds_on_override()
    {
        var db = TestAppDbContext.Create();
        var seed = await ManufacturingTestSeed.CreateAsync(db, BalanceAction.Warn);

        await ManufacturingTestSeed.ReceiveStockAsync(db, seed, seed.RawProductId, 20m, 60m, Day1);

        var journalId = await CreateAsync(
            db, seed, outputQuantity: 10m, rawQuantity: 20m, includeSecondRawMaterial: true);

        // The shortfall here is on the SECOND raw material, which has no stock at all.
        await Assert.ThrowsAsync<StockAvailabilityWarningException>(
            () => ApproveAsync(db, seed, journalId, overrideWarning: false));

        await ManufacturingTestSeed.ReceiveStockAsync(db, seed, seed.SecondRawProductId, 5m, 10m, Day1);
        var result = await ApproveAsync(db, seed, journalId, overrideWarning: true);

        Assert.Equal(1250m, result.RawMaterialCost);
    }

    [Fact]
    public async Task Approval_fails_when_the_production_cost_account_is_not_configured()
    {
        var db = TestAppDbContext.Create();
        var seed = await ManufacturingTestSeed.CreateAsync(db);

        var settings = await db.TenantSettings.SingleAsync(x => x.OrganizationId == seed.OrganizationId);
        settings.SetInventoryDefaults(seed.InventoryAccountId, null, null, null);
        await db.SaveChangesAsync(CancellationToken.None);

        await ManufacturingTestSeed.ReceiveStockAsync(db, seed, seed.RawProductId, 100m, 60m, Day1);

        var error = await Assert.ThrowsAsync<ConflictException>(
            () => CreateAndApproveAsync(db, seed, outputQuantity: 10m, rawQuantity: 20m));

        Assert.Contains("Production Cost account", error.Message, StringComparison.Ordinal);
    }

    private static async Task<Guid> CreateAsync(
        IAppDbContext db,
        ManufacturingSeed seed,
        decimal outputQuantity,
        decimal rawQuantity,
        decimal expenseAmount = 0m,
        decimal byProductPct = 0m,
        decimal byProductQuantity = 0m,
        bool includeSecondRawMaterial = false)
    {
        List<ProductionRawMaterialLineInput> rawMaterials =
            [new ProductionRawMaterialLineInput(seed.RawProductId, rawQuantity)];

        if (includeSecondRawMaterial)
        {
            rawMaterials.Add(new ProductionRawMaterialLineInput(seed.SecondRawProductId, 5m));
        }

        var created = await new CreateProductionJournalCommandHandler(db).Handle(
            new CreateProductionJournalCommand(
                seed.OrganizationId, RunDay, null, seed.FinishedProductId, outputQuantity, seed.WarehouseId,
                null, null, null, null,
                rawMaterials,
                byProductQuantity > 0
                    ? [new ProductionByProductLineInput(seed.ByProductId, byProductPct, byProductQuantity)]
                    : [],
                expenseAmount > 0 ? [new ProductionExpenseLineInput(seed.CostTermId, expenseAmount)] : []),
            CancellationToken.None);

        return created.Id;
    }

    private static Task<ApproveProductionJournalResult> ApproveAsync(
        IAppDbContext db, ManufacturingSeed seed, Guid journalId, bool overrideWarning = false) =>
        new ApproveProductionJournalCommandHandler(
            db, seed.NumberGenerator, new FakeCurrentUserService(Guid.NewGuid()), new ProductionJournalPostingRule(),
            new StockLedgerService(db), new FifoStockAvailabilityPolicy(db, new StockLedgerService(db)))
            .Handle(new ApproveProductionJournalCommand(seed.OrganizationId, journalId, overrideWarning),
                CancellationToken.None);

    private static async Task<ApproveProductionJournalResult> CreateAndApproveAsync(
        IAppDbContext db,
        ManufacturingSeed seed,
        decimal outputQuantity,
        decimal rawQuantity,
        decimal expenseAmount = 0m,
        decimal byProductPct = 0m,
        decimal byProductQuantity = 0m)
    {
        var journalId = await CreateAsync(
            db, seed, outputQuantity, rawQuantity, expenseAmount, byProductPct, byProductQuantity);
        return await ApproveAsync(db, seed, journalId);
    }
}
