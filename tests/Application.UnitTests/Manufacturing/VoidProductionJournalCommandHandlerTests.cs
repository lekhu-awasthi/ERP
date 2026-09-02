using ErpApp.Application.Common.Exceptions;
using ErpApp.Application.Common.Persistence;
using ErpApp.Application.Inventory.Stock;
using ErpApp.Application.Manufacturing;
using ErpApp.Application.Manufacturing.Commands.ApproveProductionJournal;
using ErpApp.Application.Manufacturing.Commands.CreateProductionJournal;
using ErpApp.Application.Manufacturing.Commands.VoidProductionJournal;
using ErpApp.Application.Manufacturing.Posting;
using ErpApp.Application.Sales.Stock;
using ErpApp.Application.UnitTests.TestSupport;
using ErpApp.Domain.Common;
using ErpApp.Domain.Manufacturing;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.UnitTests.Manufacturing;

/// <summary>
/// A production Void has to unwind in <b>both</b> directions, which no other Void in this codebase
/// does -- and must refuse outright once the goods it created have been consumed onward, because
/// the consuming document's COGS was computed from the very cost this would erase.
/// </summary>
public class VoidProductionJournalCommandHandlerTests
{
    private static readonly DateOnly Day1 = new(2026, 1, 10);
    private static readonly DateOnly RunDay = new(2026, 1, 25);

    [Fact]
    public async Task Voiding_puts_the_raw_materials_back_and_removes_the_goods_it_created()
    {
        var db = TestAppDbContext.Create();
        var seed = await ManufacturingTestSeed.CreateAsync(db);
        var ledger = new StockLedgerService(db);

        await ManufacturingTestSeed.ReceiveStockAsync(db, seed, seed.RawProductId, 100m, 60m, Day1);
        var approved = await CreateAndApproveAsync(db, seed, byProductQuantity: 6m, byProductPct: 20m, expenseAmount: 300m);

        Assert.Equal(80m, await AvailableAsync(ledger, seed, seed.RawProductId));
        Assert.Equal(10m, await AvailableAsync(ledger, seed, seed.FinishedProductId));
        Assert.Equal(6m, await AvailableAsync(ledger, seed, seed.ByProductId));

        await VoidAsync(db, seed, approved.Id);

        // Raw material is back at its original 100, at the cost it left at.
        Assert.Equal(100m, await AvailableAsync(ledger, seed, seed.RawProductId));

        // Finished goods and by-product are gone again.
        Assert.Equal(0m, await AvailableAsync(ledger, seed, seed.FinishedProductId));
        Assert.Equal(0m, await AvailableAsync(ledger, seed, seed.ByProductId));

        var journal = await db.ProductionJournals.SingleAsync(x => x.Id == approved.Id);
        Assert.Equal(ProductionJournalStatus.Void, journal.Status);
    }

    [Fact]
    public async Task Voiding_nets_the_general_ledger_back_to_zero_on_every_account()
    {
        var db = TestAppDbContext.Create();
        var seed = await ManufacturingTestSeed.CreateAsync(db);

        await ManufacturingTestSeed.ReceiveStockAsync(db, seed, seed.RawProductId, 100m, 60m, Day1);
        var approved = await CreateAndApproveAsync(db, seed, byProductQuantity: 6m, byProductPct: 20m, expenseAmount: 300m);

        await VoidAsync(db, seed, approved.Id);

        // Both entries live under the same SourceDocumentId, so summing them is the whole story --
        // GlJournalEntry.PostReversalOf mirrors the original's own lines rather than re-deriving.
        var glLines = await ManufacturingTestSeed.GlLinesForAsync(
            db, seed.OrganizationId, DocumentType.ProductionJournal, approved.Id);

        Assert.Equal(0m, ManufacturingTestSeed.NetMovement(glLines, seed.InventoryAccountId));
        Assert.Equal(0m, ManufacturingTestSeed.NetMovement(glLines, seed.ProductionCostAccountId));
        Assert.Equal(glLines.Sum(x => x.Debit), glLines.Sum(x => x.Credit));
    }

    [Fact]
    public async Task Voiding_is_refused_once_the_finished_goods_have_been_partly_consumed()
    {
        var db = TestAppDbContext.Create();
        var seed = await ManufacturingTestSeed.CreateAsync(db);
        var ledger = new StockLedgerService(db);

        await ManufacturingTestSeed.ReceiveStockAsync(db, seed, seed.RawProductId, 100m, 60m, Day1);
        var approved = await CreateAndApproveAsync(db, seed);

        // Something else takes 4 of the 10 finished units -- a sale, in real life.
        await ledger.ConsumeAsync(
            seed.OrganizationId, seed.FinishedProductId, seed.WarehouseId, 4m,
            DocumentType.Invoice, Guid.NewGuid(), RunDay, CancellationToken.None);
        await db.SaveChangesAsync(CancellationToken.None);

        var error = await Assert.ThrowsAsync<ConflictException>(() => VoidAsync(db, seed, approved.Id));
        Assert.Contains("already been consumed", error.Message, StringComparison.Ordinal);

        // And nothing was half-unwound: the run is still Approved and the raw material still gone.
        var journal = await db.ProductionJournals.SingleAsync(x => x.Id == approved.Id);
        Assert.Equal(ProductionJournalStatus.Approved, journal.Status);
        Assert.Equal(80m, await AvailableAsync(ledger, seed, seed.RawProductId));
    }

    [Fact]
    public async Task A_draft_production_journal_cannot_be_voided()
    {
        var db = TestAppDbContext.Create();
        var seed = await ManufacturingTestSeed.CreateAsync(db);

        await ManufacturingTestSeed.ReceiveStockAsync(db, seed, seed.RawProductId, 100m, 60m, Day1);
        var created = await CreateAsync(db, seed);

        await Assert.ThrowsAsync<ConflictException>(() => VoidAsync(db, seed, created));
    }

    private static Task<decimal> AvailableAsync(IStockLedgerService ledger, ManufacturingSeed seed, Guid productId) =>
        ledger.GetAvailableQuantityAsync(seed.OrganizationId, productId, seed.WarehouseId, CancellationToken.None);

    private static async Task<Guid> CreateAsync(
        IAppDbContext db, ManufacturingSeed seed, decimal expenseAmount = 0m,
        decimal byProductPct = 0m, decimal byProductQuantity = 0m)
    {
        var created = await new CreateProductionJournalCommandHandler(db).Handle(
            new CreateProductionJournalCommand(
                seed.OrganizationId, RunDay, null, seed.FinishedProductId, 10m, seed.WarehouseId, null, null, null, null,
                [new ProductionRawMaterialLineInput(seed.RawProductId, 20m)],
                byProductQuantity > 0
                    ? [new ProductionByProductLineInput(seed.ByProductId, byProductPct, byProductQuantity)]
                    : [],
                expenseAmount > 0 ? [new ProductionExpenseLineInput(seed.CostTermId, expenseAmount)] : []),
            CancellationToken.None);

        return created.Id;
    }

    private static async Task<ApproveProductionJournalResult> CreateAndApproveAsync(
        IAppDbContext db, ManufacturingSeed seed, decimal expenseAmount = 0m,
        decimal byProductPct = 0m, decimal byProductQuantity = 0m)
    {
        var id = await CreateAsync(db, seed, expenseAmount, byProductPct, byProductQuantity);

        return await new ApproveProductionJournalCommandHandler(
            db, seed.NumberGenerator, new FakeCurrentUserService(Guid.NewGuid()), new ProductionJournalPostingRule(),
            new StockLedgerService(db), new FifoStockAvailabilityPolicy(db, new StockLedgerService(db)))
            .Handle(new ApproveProductionJournalCommand(seed.OrganizationId, id), CancellationToken.None);
    }

    private static Task<VoidProductionJournalResult> VoidAsync(IAppDbContext db, ManufacturingSeed seed, Guid id) =>
        new VoidProductionJournalCommandHandler(
            db, new FakeCurrentUserService(Guid.NewGuid()), new StockLedgerService(db))
            .Handle(new VoidProductionJournalCommand(seed.OrganizationId, id), CancellationToken.None);
}
