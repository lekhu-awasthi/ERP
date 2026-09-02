using ErpApp.Application.Common.Persistence;
using ErpApp.Application.Inventory.Stock;
using ErpApp.Application.Manufacturing;
using ErpApp.Application.Manufacturing.Commands.ApproveProductionJournal;
using ErpApp.Application.Manufacturing.Commands.CreateBillOfMaterials;
using ErpApp.Application.Manufacturing.Commands.CreateProductionJournal;
using ErpApp.Application.Manufacturing.Posting;
using ErpApp.Application.Manufacturing.Queries.GetBomTemplate;
using ErpApp.Application.Manufacturing.Queries.ProductionPlanning;
using ErpApp.Application.Manufacturing.Queries.ProductionSummary;
using ErpApp.Application.Manufacturing.Queries.ProductionVariance;
using ErpApp.Application.Sales.Stock;
using ErpApp.Application.UnitTests.TestSupport;

namespace ErpApp.Application.UnitTests.Manufacturing;

/// <summary>
/// The three manufacturing reports and the LOAD BOM template, all four of whose shapes were read
/// off the live reference product on 2026-09-02 rather than designed here.
/// </summary>
public class ManufacturingReportQueryHandlerTests
{
    private static readonly DateOnly Day1 = new(2026, 1, 10);
    private static readonly DateOnly RunDay = new(2026, 1, 25);
    private static readonly DateOnly From = new(2026, 1, 1);
    private static readonly DateOnly To = new(2026, 1, 31);

    [Fact]
    public async Task Load_bom_scales_quantities_and_expenses_by_output_but_never_the_percentage()
    {
        var db = TestAppDbContext.Create();
        var seed = await ManufacturingTestSeed.CreateAsync(db);

        // The BOM read live: output 12, raw 12, by-product 15 at 12%, expense 500. Asking for 24
        // must give 24 / 30 / 12% / 1000.
        await CreateBomAsync(db, seed, outputQuantity: 12m, rawQuantity: 12m, byProductQuantity: 15m,
            byProductPct: 12m, expenseAmount: 500m);

        var template = await new GetBomTemplateQueryHandler(db).Handle(
            new GetBomTemplateQuery(seed.OrganizationId, seed.FinishedProductId, 24m), CancellationToken.None);

        Assert.NotNull(template);
        Assert.Equal(24m, template!.RawMaterials.Single().Quantity);
        Assert.Equal(30m, template.ByProducts.Single().Quantity);
        Assert.Equal(12m, template.ByProducts.Single().CostAllocationPct);
        Assert.Equal(1000m, template.Expenses.Single().Amount);
    }

    [Fact]
    public async Task Load_bom_returns_nothing_rather_than_failing_when_the_product_has_no_recipe()
    {
        var db = TestAppDbContext.Create();
        var seed = await ManufacturingTestSeed.CreateAsync(db);

        var template = await new GetBomTemplateQueryHandler(db).Handle(
            new GetBomTemplateQuery(seed.OrganizationId, seed.FinishedProductId, 10m), CancellationToken.None);

        Assert.Null(template);
    }

    [Fact]
    public async Task The_summary_report_carries_every_roll_up_figure_and_totals_over_the_whole_filtered_set()
    {
        var db = TestAppDbContext.Create();
        var seed = await ManufacturingTestSeed.CreateAsync(db);

        await ManufacturingTestSeed.ReceiveStockAsync(db, seed, seed.RawProductId, 200m, 60m, Day1);
        await CreateAndApproveJournalAsync(db, seed, expenseAmount: 300m, byProductPct: 20m, byProductQuantity: 6m);
        await CreateAndApproveJournalAsync(db, seed, expenseAmount: 100m);

        var report = await new ProductionSummaryQueryHandler(db).Handle(
            new ProductionSummaryQuery(seed.OrganizationId, From, To, null, null), CancellationToken.None);

        Assert.Equal(2, report.Rows.Items.Count);

        var withByProduct = report.Rows.Items.Single(x => x.ByProducts.Count == 1);
        Assert.Equal(1200m, withByProduct.RawMaterialCost);
        Assert.Equal(300m, withByProduct.ProductionExpenseCost);
        Assert.Equal(1500m, withByProduct.TotalCostOfProduction);
        Assert.Equal(300m, withByProduct.CostAllocatedToByProduct);
        Assert.Equal(1200m, withByProduct.FinishedGoodsCost);
        Assert.Equal(120m, withByProduct.FinishedGood.Rate);
        Assert.Equal("Direct Labor Costs", withByProduct.Expenses.Single().CostTermName);

        // Totals come from the whole filtered set, not the page (phase-16c bug #1).
        Assert.Equal(2400m, report.Totals.RawMaterialCost);
        Assert.Equal(400m, report.Totals.ProductionExpenseCost);
        Assert.Equal(300m, report.Totals.CostAllocatedToByProduct);
        Assert.Equal(2500m, report.Totals.FinishedGoodsCost);

        // Conservation across the whole report: value in equals value out.
        Assert.Equal(
            report.Totals.RawMaterialCost + report.Totals.ProductionExpenseCost,
            report.Totals.FinishedGoodsCost + report.Totals.CostAllocatedToByProduct);
    }

    [Fact]
    public async Task The_summary_reports_totals_survive_paging_to_a_single_row()
    {
        var db = TestAppDbContext.Create();
        var seed = await ManufacturingTestSeed.CreateAsync(db);

        await ManufacturingTestSeed.ReceiveStockAsync(db, seed, seed.RawProductId, 200m, 60m, Day1);
        await CreateAndApproveJournalAsync(db, seed, expenseAmount: 300m);
        await CreateAndApproveJournalAsync(db, seed, expenseAmount: 100m);

        var report = await new ProductionSummaryQueryHandler(db).Handle(
            new ProductionSummaryQuery(seed.OrganizationId, From, To, null, null, ExportAll: false, Page: 1, PageSize: 1),
            CancellationToken.None);

        Assert.Single(report.Rows.Items);
        Assert.Equal(2, report.Rows.TotalCount);

        // The figure a client-side reduce over this page would have produced is 1200, not 2400.
        Assert.Equal(2400m, report.Totals.RawMaterialCost);
    }

    [Fact]
    public async Task The_variance_report_scales_the_bom_plan_to_the_runs_own_output_before_comparing()
    {
        var db = TestAppDbContext.Create();
        var seed = await ManufacturingTestSeed.CreateAsync(db);

        // The reference tenant's BOTTLEE case: a BOM whose output is 12 and whose raw material is
        // 12 (a 1:1 ratio), and a run producing 10 that used only 8.
        var bomId = await CreateBomAsync(db, seed, outputQuantity: 12m, rawQuantity: 12m);

        await ManufacturingTestSeed.ReceiveStockAsync(db, seed, seed.RawProductId, 100m, 60m, Day1);
        await CreateAndApproveJournalAsync(db, seed, outputQuantity: 10m, rawQuantity: 8m, billOfMaterialsId: bomId);

        var report = await new ProductionVarianceQueryHandler(db).Handle(
            new ProductionVarianceQuery(seed.OrganizationId, From, To, null, null), CancellationToken.None);

        var line = Assert.Single(Assert.Single(report.Items).Lines);

        // Plan for a 10-unit run is 10, not the BOM's own 12 -- the live report reported 12.5 here
        // and a 36% variance, which compares two different batch sizes.
        Assert.Equal(10m, line.BomQuantity);
        Assert.Equal(8m, line.VoucherQuantity);
        Assert.Equal(2m, line.VarianceQuantity);
        Assert.Equal(20m, line.VariancePct);
    }

    [Fact]
    public async Task The_variance_report_lists_only_runs_that_carry_a_bill_of_materials()
    {
        var db = TestAppDbContext.Create();
        var seed = await ManufacturingTestSeed.CreateAsync(db);

        await ManufacturingTestSeed.ReceiveStockAsync(db, seed, seed.RawProductId, 100m, 60m, Day1);
        await CreateAndApproveJournalAsync(db, seed);

        var report = await new ProductionVarianceQueryHandler(db).Handle(
            new ProductionVarianceQuery(seed.OrganizationId, From, To, null, null), CancellationToken.None);

        Assert.Empty(report.Items);
    }

    [Fact]
    public async Task The_planning_report_explodes_the_bom_and_compares_it_against_stock_on_hand()
    {
        var db = TestAppDbContext.Create();
        var seed = await ManufacturingTestSeed.CreateAsync(db);

        await CreateBomAsync(db, seed, outputQuantity: 12m, rawQuantity: 12m);
        await ManufacturingTestSeed.ReceiveStockAsync(db, seed, seed.RawProductId, 8896.5m, 60m, Day1);

        var report = await new ProductionPlanningQueryHandler(db).Handle(
            new ProductionPlanningQuery(seed.OrganizationId, seed.FinishedProductId, 10m, null), CancellationToken.None);

        // Exactly the live report's own BOTTLEE figures: 10 required, 8896.5 available, 8886.5 spare.
        var line = Assert.Single(report.Lines);
        Assert.Equal(10m, line.QuantityRequired);
        Assert.Equal(8896.5m, line.QuantityAvailable);
        Assert.Equal(8886.5m, line.Surplus);
        Assert.False(report.MultipleLevel);
    }

    [Fact]
    public async Task The_planning_report_reports_a_deficiency_and_can_be_narrowed_to_one_warehouse()
    {
        var db = TestAppDbContext.Create();
        var seed = await ManufacturingTestSeed.CreateAsync(db);

        await CreateBomAsync(db, seed, outputQuantity: 10m, rawQuantity: 20m);
        await ManufacturingTestSeed.ReceiveStockAsync(db, seed, seed.RawProductId, 5m, 60m, Day1);
        await ManufacturingTestSeed.ReceiveStockAsync(
            db, seed, seed.RawProductId, 50m, 60m, Day1, seed.OtherWarehouseId);

        var allWarehouses = await new ProductionPlanningQueryHandler(db).Handle(
            new ProductionPlanningQuery(seed.OrganizationId, seed.FinishedProductId, 10m, null), CancellationToken.None);
        Assert.Equal(55m, allWarehouses.Lines.Single().QuantityAvailable);
        Assert.Equal(35m, allWarehouses.Lines.Single().Surplus);

        // Narrowed to the warehouse the run would actually consume from, it is 15 short.
        var oneWarehouse = await new ProductionPlanningQueryHandler(db).Handle(
            new ProductionPlanningQuery(seed.OrganizationId, seed.FinishedProductId, 10m, seed.WarehouseId),
            CancellationToken.None);
        Assert.Equal(5m, oneWarehouse.Lines.Single().QuantityAvailable);
        Assert.Equal(-15m, oneWarehouse.Lines.Single().Surplus);
    }

    private static async Task<Guid> CreateBomAsync(
        IAppDbContext db,
        ManufacturingSeed seed,
        decimal outputQuantity,
        decimal rawQuantity,
        decimal byProductQuantity = 0m,
        decimal byProductPct = 0m,
        decimal expenseAmount = 0m)
    {
        var created = await new CreateBillOfMaterialsCommandHandler(db).Handle(
            new CreateBillOfMaterialsCommand(
                seed.OrganizationId, seed.FinishedProductId, outputQuantity, false, null,
                [new ProductionRawMaterialLineInput(seed.RawProductId, rawQuantity)],
                byProductQuantity > 0
                    ? [new ProductionByProductLineInput(seed.ByProductId, byProductPct, byProductQuantity)]
                    : [],
                expenseAmount > 0 ? [new ProductionExpenseLineInput(seed.CostTermId, expenseAmount)] : []),
            CancellationToken.None);

        return created.Id;
    }

    private static async Task<ApproveProductionJournalResult> CreateAndApproveJournalAsync(
        IAppDbContext db,
        ManufacturingSeed seed,
        decimal outputQuantity = 10m,
        decimal rawQuantity = 20m,
        decimal expenseAmount = 0m,
        decimal byProductPct = 0m,
        decimal byProductQuantity = 0m,
        Guid? billOfMaterialsId = null)
    {
        var created = await new CreateProductionJournalCommandHandler(db).Handle(
            new CreateProductionJournalCommand(
                seed.OrganizationId, RunDay, null, seed.FinishedProductId, outputQuantity, seed.WarehouseId,
                billOfMaterialsId, null, null, null,
                [new ProductionRawMaterialLineInput(seed.RawProductId, rawQuantity)],
                byProductQuantity > 0
                    ? [new ProductionByProductLineInput(seed.ByProductId, byProductPct, byProductQuantity)]
                    : [],
                expenseAmount > 0 ? [new ProductionExpenseLineInput(seed.CostTermId, expenseAmount)] : []),
            CancellationToken.None);

        return await new ApproveProductionJournalCommandHandler(
            db, seed.NumberGenerator, new FakeCurrentUserService(Guid.NewGuid()), new ProductionJournalPostingRule(),
            new StockLedgerService(db), new FifoStockAvailabilityPolicy(db, new StockLedgerService(db)))
            .Handle(new ApproveProductionJournalCommand(seed.OrganizationId, created.Id), CancellationToken.None);
    }
}
