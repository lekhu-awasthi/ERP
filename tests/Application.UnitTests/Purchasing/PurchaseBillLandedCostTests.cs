using ErpApp.Application.Accounting.Commands.CreateAccount;
using ErpApp.Application.Accounting.Commands.CreateAccountGroup;
using ErpApp.Application.Catalog.Commands.CreateProduct;
using ErpApp.Application.Catalog.Commands.CreateProductCategory;
using ErpApp.Application.Catalog.Commands.CreateUnitOfMeasurement;
using ErpApp.Application.Common.Exceptions;
using ErpApp.Application.Common.Persistence;
using ErpApp.Application.Configuration.Commands.CreateCostTerm;
using ErpApp.Application.Contacts.Commands.CreateContact;
using ErpApp.Application.Inventory.Stock;
using ErpApp.Application.Purchasing;
using ErpApp.Application.Purchasing.Commands.ApproveDebitNote;
using ErpApp.Application.Purchasing.Commands.ApprovePurchaseBill;
using ErpApp.Application.Purchasing.Commands.CreateDebitNote;
using ErpApp.Application.Purchasing.Commands.CreatePurchaseBill;
using ErpApp.Application.Purchasing.Commands.VoidPurchaseBill;
using ErpApp.Application.Purchasing.Posting;
using ErpApp.Application.Purchasing.Queries.GetPurchaseBill;
using ErpApp.Application.Tenancy.Commands.CreateWarehouse;
using ErpApp.Application.UnitTests.TestSupport;
using ErpApp.Domain.Accounting;
using ErpApp.Domain.Catalog;
using ErpApp.Domain.Common;
using ErpApp.Domain.Configuration;
using ErpApp.Domain.Contacts;
using ErpApp.Domain.Purchasing;
using ErpApp.Domain.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.UnitTests.Purchasing;

/// <summary>
/// Phase 29 (FR-6.15) -- landed cost end to end through the Approve handler: the allocation reaches
/// the FIFO layers, the conservation law holds, the GL entry balances and agrees with the ledger,
/// and Void unwinds all of it.
/// </summary>
public class PurchaseBillLandedCostTests
{
    [Fact]
    public async Task The_allocation_is_capitalised_into_the_fifo_layers()
    {
        var db = TestAppDbContext.Create();
        var seed = await SeedAsync(db);

        // 10 @ 600 = 6,000 and 5 @ 120 = 600, plus 660 of Freight spread by value: 600 and 60.
        // Landed unit costs are therefore 660.00 and 132.00, not 600 and 120.
        var billId = await CreateAsync(
            db, seed,
            [
                new PurchaseBillLineInput(seed.GoodsAId, 10m, 600m, VatRate.NoVat, ExpenditureClassification.Others),
                new PurchaseBillLineInput(seed.GoodsBId, 5m, 120m, VatRate.NoVat, ExpenditureClassification.Others),
            ],
            [new PurchaseBillAdditionalCostInput(seed.FreightCostTermId, null, AdditionalCostMethod.Value, 660m)]);

        await ApproveAsync(db, seed, billId);

        var layers = await db.StockLedgerEntries
            .Where(x => x.SourceDocumentType == DocumentType.PurchaseBill && x.SourceDocumentId == billId)
            .ToListAsync();

        Assert.Equal(660m, layers.Single(x => x.ProductId == seed.GoodsAId).UnitCost);
        Assert.Equal(132m, layers.Single(x => x.ProductId == seed.GoodsBId).UnitCost);
    }

    [Fact]
    public async Task The_conservation_law_holds_and_the_residue_is_named()
    {
        var db = TestAppDbContext.Create();
        var seed = await SeedAsync(db);

        // 3 units at 100 with 100 of Freight: 33.3333 per line is exact at the allocation scale, but
        // the landed unit cost 133.3333 x 3 = 399.9999 is a paisa-and-a-bit short of 400. That gap
        // is the phase's residue and it is reported, not absorbed.
        var billId = await CreateAsync(
            db, seed,
            [new PurchaseBillLineInput(seed.GoodsAId, 3m, 100m, VatRate.NoVat, ExpenditureClassification.Others)],
            [new PurchaseBillAdditionalCostInput(seed.FreightCostTermId, null, AdditionalCostMethod.Value, 100m)]);

        var approved = await ApproveAsync(db, seed, billId);

        var layers = await db.StockLedgerEntries
            .Where(x => x.SourceDocumentType == DocumentType.PurchaseBill && x.SourceDocumentId == billId)
            .ToListAsync();
        var layerValue = layers.Sum(x => x.UnitCost * x.QuantityIn);

        Assert.Equal(399.9999m, layerValue);
        Assert.Equal(99.9999m, approved.CapitalisedAdditionalCost);
        Assert.Equal(0.0001m, approved.AdditionalCostRoundingAdjustment);

        //     goods value  +  additional cost  =  layer value  +  residue
        Assert.Equal(
            300m + 100m,
            layerValue + approved.AdditionalCostRoundingAdjustment!.Value);
    }

    [Fact]
    public async Task The_gl_entry_balances_and_the_inventory_account_equals_the_ledger()
    {
        var db = TestAppDbContext.Create();
        var seed = await SeedAsync(db);

        var billId = await CreateAsync(
            db, seed,
            [
                new PurchaseBillLineInput(seed.GoodsAId, 10m, 600m, VatRate.NoVat, ExpenditureClassification.Others),
                new PurchaseBillLineInput(seed.GoodsBId, 5m, 120m, VatRate.NoVat, ExpenditureClassification.Others),
            ],
            [new PurchaseBillAdditionalCostInput(seed.FreightCostTermId, null, AdditionalCostMethod.Value, 660m)]);

        await ApproveAsync(db, seed, billId);

        var glLines = await GlLinesForAsync(db, seed.OrganizationId, billId);

        Assert.Equal(glLines.Sum(x => x.Debit), glLines.Sum(x => x.Credit));

        // Inventory carries the goods amounts plus the capitalised cost, and that total is exactly
        // the value of the layers created -- the property the whole design exists to hold.
        var layerValue = await db.StockLedgerEntries
            .Where(x => x.SourceDocumentType == DocumentType.PurchaseBill && x.SourceDocumentId == billId)
            .SumAsync(x => x.UnitCost * x.QuantityIn);
        var inventoryNet = glLines.Where(x => x.AccountId == seed.InventoryAccountId).Sum(x => x.Debit - x.Credit);
        Assert.Equal(layerValue, inventoryNet);

        // The clearing account carries the whole additional cost and nothing else...
        Assert.Equal(660m, glLines.Where(x => x.AccountId == seed.ClearingAccountId).Sum(x => x.Credit - x.Debit));

        // ...and the supplier is credited the goods total only, never a paisa of the freight.
        Assert.Equal(6600m, glLines.Where(x => x.AccountId == seed.AccountsPayableId).Sum(x => x.Credit - x.Debit));
    }

    [Fact]
    public async Task Quantity_method_ignores_the_line_values()
    {
        var db = TestAppDbContext.Create();
        var seed = await SeedAsync(db);

        var billId = await CreateAsync(
            db, seed,
            [
                new PurchaseBillLineInput(seed.GoodsAId, 10m, 600m, VatRate.NoVat, ExpenditureClassification.Others),
                new PurchaseBillLineInput(seed.GoodsBId, 5m, 120m, VatRate.NoVat, ExpenditureClassification.Others),
            ],
            [new PurchaseBillAdditionalCostInput(seed.FreightCostTermId, null, AdditionalCostMethod.Quantity, 660m)]);

        await ApproveAsync(db, seed, billId);

        var layers = await db.StockLedgerEntries
            .Where(x => x.SourceDocumentType == DocumentType.PurchaseBill && x.SourceDocumentId == billId)
            .ToListAsync();

        // 440 over 10 units and 220 over 5 -- the same 44 per unit either way, which is the point.
        Assert.Equal(644m, layers.Single(x => x.ProductId == seed.GoodsAId).UnitCost);
        Assert.Equal(164m, layers.Single(x => x.ProductId == seed.GoodsBId).UnitCost);
    }

    [Fact]
    public async Task A_bill_with_no_additional_cost_posts_exactly_what_it_always_did()
    {
        var db = TestAppDbContext.Create();
        var seed = await SeedAsync(db);

        var billId = await CreateAsync(
            db, seed,
            [new PurchaseBillLineInput(seed.GoodsAId, 10m, 600m, VatRate.NoVat, ExpenditureClassification.Others)],
            []);

        var approved = await ApproveAsync(db, seed, billId);

        Assert.Null(approved.CapitalisedAdditionalCost);
        Assert.Null(approved.AdditionalCostRoundingAdjustment);

        var glLines = await GlLinesForAsync(db, seed.OrganizationId, billId);
        Assert.Equal(2, glLines.Count);
        Assert.DoesNotContain(glLines, x => x.AccountId == seed.ClearingAccountId);
        Assert.Equal(600m, (await db.StockLedgerEntries.SingleAsync(
            x => x.SourceDocumentType == DocumentType.PurchaseBill && x.SourceDocumentId == billId)).UnitCost);
    }

    [Fact]
    public async Task Approving_without_a_clearing_account_is_a_conflict_and_creates_no_layer()
    {
        var db = TestAppDbContext.Create();
        var seed = await SeedAsync(db, withClearingAccount: false);

        var billId = await CreateAsync(
            db, seed,
            [new PurchaseBillLineInput(seed.GoodsAId, 10m, 600m, VatRate.NoVat, ExpenditureClassification.Others)],
            [new PurchaseBillAdditionalCostInput(seed.FreightCostTermId, null, AdditionalCostMethod.Value, 660m)]);

        await Assert.ThrowsAsync<ConflictException>(() => ApproveAsync(db, seed, billId));

        Assert.False(await db.StockLedgerEntries.AnyAsync(
            x => x.SourceDocumentType == DocumentType.PurchaseBill && x.SourceDocumentId == billId));
    }

    [Fact]
    public async Task Void_releases_the_layers_it_created_capitalised_cost_included()
    {
        var db = TestAppDbContext.Create();
        var seed = await SeedAsync(db);

        var billId = await CreateAsync(
            db, seed,
            [new PurchaseBillLineInput(seed.GoodsAId, 10m, 600m, VatRate.NoVat, ExpenditureClassification.Others)],
            [new PurchaseBillAdditionalCostInput(seed.FreightCostTermId, null, AdditionalCostMethod.Value, 660m)]);

        await ApproveAsync(db, seed, billId);

        await new VoidPurchaseBillCommandHandler(db, new FakeCurrentUserService(Guid.NewGuid()), new StockLedgerService(db))
            .Handle(new VoidPurchaseBillCommand(seed.OrganizationId, billId), CancellationToken.None);

        // Nothing left on hand...
        Assert.Equal(0m, await db.StockLedgerEntries
            .Where(x => x.SourceDocumentType == DocumentType.PurchaseBill && x.SourceDocumentId == billId)
            .SumAsync(x => x.QuantityRemaining));

        // ...and every account, the clearing account included, back to zero across the pair.
        var entries = await db.GlJournalEntries.Include(x => x.Lines)
            .Where(x => x.SourceDocumentType == DocumentType.PurchaseBill && x.SourceDocumentId == billId)
            .ToListAsync();
        Assert.Equal(2, entries.Count);

        foreach (var accountId in new[] { seed.InventoryAccountId, seed.ClearingAccountId, seed.AccountsPayableId })
        {
            var net = entries.SelectMany(x => x.Lines).Where(x => x.AccountId == accountId).Sum(x => x.Debit - x.Credit);
            Assert.Equal(0m, net);
        }
    }

    [Fact]
    public async Task The_allocation_is_readable_afterwards_as_a_product_by_cost_term_matrix()
    {
        var db = TestAppDbContext.Create();
        var seed = await SeedAsync(db);

        var billId = await CreateAsync(
            db, seed,
            [
                new PurchaseBillLineInput(seed.GoodsAId, 10m, 600m, VatRate.NoVat, ExpenditureClassification.Others),
                new PurchaseBillLineInput(seed.GoodsBId, 5m, 120m, VatRate.NoVat, ExpenditureClassification.Others),
            ],
            [new PurchaseBillAdditionalCostInput(seed.FreightCostTermId, null, AdditionalCostMethod.Value, 660m)]);

        await ApproveAsync(db, seed, billId);

        var dto = await new GetPurchaseBillQueryHandler(db).Handle(
            new GetPurchaseBillQuery(seed.OrganizationId, billId), CancellationToken.None);

        var row = Assert.Single(dto.AdditionalCosts);
        Assert.Equal(seed.FreightCostTermId, row.CostTermId);
        Assert.Equal(660m, dto.AdditionalCostTotal);
        Assert.Equal(2, row.Allocations.Count);
        Assert.Equal(660m, row.Allocations.Sum(x => x.Amount));
        Assert.Equal(660m, dto.CapitalisedAdditionalCost);
        Assert.Equal(0m, dto.AdditionalCostRoundingAdjustment);

        // ...and it is not folded into the document total.
        Assert.Equal(6600m, dto.GrandTotal);
    }

    [Fact]
    public async Task A_row_naming_a_service_product_is_refused_at_create()
    {
        var db = TestAppDbContext.Create();
        var seed = await SeedAsync(db);

        await Assert.ThrowsAsync<ConflictException>(() => CreateAsync(
            db, seed,
            [
                new PurchaseBillLineInput(seed.GoodsAId, 10m, 600m, VatRate.NoVat, ExpenditureClassification.Others),
                new PurchaseBillLineInput(seed.ServiceId, 1m, 900m, VatRate.NoVat, ExpenditureClassification.Others),
            ],
            [new PurchaseBillAdditionalCostInput(seed.FreightCostTermId, seed.ServiceId, AdditionalCostMethod.Value, 100m)]));
    }

    [Fact]
    public async Task A_production_cost_term_is_not_selectable_here()
    {
        // Phase 20c split the lookup in two on purpose; the ProductionCost half is Phase 25's.
        var db = TestAppDbContext.Create();
        var seed = await SeedAsync(db);

        await Assert.ThrowsAsync<NotFoundException>(() => CreateAsync(
            db, seed,
            [new PurchaseBillLineInput(seed.GoodsAId, 10m, 600m, VatRate.NoVat, ExpenditureClassification.Others)],
            [new PurchaseBillAdditionalCostInput(seed.ProductionCostTermId, null, AdditionalCostMethod.Value, 100m)]));
    }

    [Fact]
    public async Task A_debit_note_releases_the_returned_units_share_of_the_capitalised_cost()
    {
        var db = TestAppDbContext.Create();
        var seed = await SeedAsync(db);

        // 10 @ 600 with 660 of Freight -- a landed unit cost of 666, so returning 4 units takes
        // 4 x 666 = 2,664 of stock value out, of which 4/10 x 660 = 264 is capitalised freight.
        var billId = await CreateAsync(
            db, seed,
            [new PurchaseBillLineInput(seed.GoodsAId, 10m, 600m, VatRate.NoVat, ExpenditureClassification.Others)],
            [new PurchaseBillAdditionalCostInput(seed.FreightCostTermId, null, AdditionalCostMethod.Value, 660m)]);
        await ApproveAsync(db, seed, billId);

        var note = await new CreateDebitNoteCommandHandler(db).Handle(
            new CreateDebitNoteCommand(
                seed.OrganizationId, seed.SupplierId, new DateOnly(2026, 1, 20), null, null,
                [new DebitNoteLineInput(seed.GoodsAId, 4m, 600m, VatRate.NoVat)],
                DocumentType.PurchaseBill, billId),
            CancellationToken.None);

        await new ApproveDebitNoteCommandHandler(
                db, seed.NumberGenerator, new FakeCurrentUserService(Guid.NewGuid()), new DebitNotePostingRule(),
                new StockLedgerService(db))
            .Handle(new ApproveDebitNoteCommand(seed.OrganizationId, note.Id), CancellationToken.None);

        var noteEntry = await db.GlJournalEntries.Include(x => x.Lines).SingleAsync(
            x => x.SourceDocumentType == DocumentType.DebitNote && x.SourceDocumentId == note.Id);
        var noteLines = noteEntry.Lines.ToList();

        Assert.Equal(noteLines.Sum(x => x.Debit), noteLines.Sum(x => x.Credit));

        // Inventory is credited the goods amount AND the freight riding in those units -- 2,400 plus
        // 264 -- which is exactly what ConsumeAsync took out of the FIFO layers (4 x 666). Crediting
        // only the 2,400 return price would leave the account permanently 264 above the ledger.
        Assert.Equal(2664m, noteLines.Where(x => x.AccountId == seed.InventoryAccountId).Sum(x => x.Credit - x.Debit));
        Assert.Equal(264m, noteLines.Where(x => x.AccountId == seed.ClearingAccountId).Sum(x => x.Debit - x.Credit));

        // The supplier is debited the return price only, unchanged by this phase.
        Assert.Equal(2400m, noteLines.Where(x => x.AccountId == seed.AccountsPayableId).Sum(x => x.Debit - x.Credit));
    }

    [Fact]
    public async Task A_full_return_nets_the_clearing_account_back_to_zero()
    {
        var db = TestAppDbContext.Create();
        var seed = await SeedAsync(db);

        var billId = await CreateAsync(
            db, seed,
            [new PurchaseBillLineInput(seed.GoodsAId, 10m, 600m, VatRate.NoVat, ExpenditureClassification.Others)],
            [new PurchaseBillAdditionalCostInput(seed.FreightCostTermId, null, AdditionalCostMethod.Value, 660m)]);
        await ApproveAsync(db, seed, billId);

        var note = await new CreateDebitNoteCommandHandler(db).Handle(
            new CreateDebitNoteCommand(
                seed.OrganizationId, seed.SupplierId, new DateOnly(2026, 1, 20), null, null,
                [new DebitNoteLineInput(seed.GoodsAId, 10m, 600m, VatRate.NoVat)],
                DocumentType.PurchaseBill, billId),
            CancellationToken.None);

        await new ApproveDebitNoteCommandHandler(
                db, seed.NumberGenerator, new FakeCurrentUserService(Guid.NewGuid()), new DebitNotePostingRule(),
                new StockLedgerService(db))
            .Handle(new ApproveDebitNoteCommand(seed.OrganizationId, note.Id), CancellationToken.None);

        // phase-6 bug #3's discipline: trace every account across the original and its reversal.
        var allLines = await db.GlJournalEntries.Include(x => x.Lines)
            .Where(x => (x.SourceDocumentType == DocumentType.PurchaseBill && x.SourceDocumentId == billId)
                || (x.SourceDocumentType == DocumentType.DebitNote && x.SourceDocumentId == note.Id))
            .SelectMany(x => x.Lines)
            .ToListAsync();

        foreach (var accountId in new[] { seed.InventoryAccountId, seed.ClearingAccountId, seed.AccountsPayableId })
        {
            Assert.Equal(0m, allLines.Where(x => x.AccountId == accountId).Sum(x => x.Debit - x.Credit));
        }
    }

    private static async Task<List<GlLine>> GlLinesForAsync(IAppDbContext db, Guid organizationId, Guid billId)
    {
        var entry = await db.GlJournalEntries.Include(x => x.Lines).SingleAsync(
            x => x.OrganizationId == organizationId
                && x.SourceDocumentType == DocumentType.PurchaseBill && x.SourceDocumentId == billId);
        return entry.Lines.ToList();
    }

    private static async Task<Guid> CreateAsync(
        IAppDbContext db,
        Seed seed,
        IReadOnlyList<PurchaseBillLineInput> lines,
        IReadOnlyList<PurchaseBillAdditionalCostInput> additionalCosts)
    {
        var created = await new CreatePurchaseBillCommandHandler(db).Handle(
            new CreatePurchaseBillCommand(
                seed.OrganizationId, seed.SupplierId, seed.WarehouseId, new DateOnly(2026, 1, 10), null, null, false,
                null, null, null, null, lines)
            {
                AdditionalCosts = additionalCosts,
            },
            CancellationToken.None);

        return created.Id;
    }

    private static Task<ApprovePurchaseBillResult> ApproveAsync(IAppDbContext db, Seed seed, Guid billId) =>
        new ApprovePurchaseBillCommandHandler(
                db, seed.NumberGenerator, new FakeCurrentUserService(Guid.NewGuid()), new PurchaseBillPostingRule(),
                new StockLedgerService(db))
            .Handle(new ApprovePurchaseBillCommand(seed.OrganizationId, billId), CancellationToken.None);

    private sealed record Seed(
        Guid OrganizationId, FakeDocumentNumberGenerator NumberGenerator, Guid SupplierId, Guid WarehouseId,
        Guid GoodsAId, Guid GoodsBId, Guid ServiceId, Guid InventoryAccountId, Guid AccountsPayableId,
        Guid ClearingAccountId, Guid FreightCostTermId, Guid ProductionCostTermId);

    private static async Task<Seed> SeedAsync(IAppDbContext db, bool withClearingAccount = true)
    {
        var organizationId = Guid.NewGuid();
        var numberGenerator = new FakeDocumentNumberGenerator();

        var supplier = await new CreateContactCommandHandler(db, numberGenerator).Handle(
            new CreateContactCommand(organizationId, ContactType.Supplier, "Global Supplies", null, null, null, null, null, 0m),
            CancellationToken.None);
        var warehouse = await new CreateWarehouseCommandHandler(db).Handle(
            new CreateWarehouseCommand(organizationId, "Main Warehouse"), CancellationToken.None);
        var category = await new CreateProductCategoryCommandHandler(db).Handle(
            new CreateProductCategoryCommand(organizationId, "General", null), CancellationToken.None);
        var unit = await new CreateUnitOfMeasurementCommandHandler(db).Handle(
            new CreateUnitOfMeasurementCommand(organizationId, "Piece", "pc"), CancellationToken.None);

        var goodsA = await new CreateProductCommandHandler(db, numberGenerator).Handle(
            new CreateProductCommand(
                organizationId, ProductType.Goods, "Motorbike", category.Id, unit.Id, null, true, 900m, 600m,
                VatRate.NoVat, 0, true),
            CancellationToken.None);
        var goodsB = await new CreateProductCommandHandler(db, numberGenerator).Handle(
            new CreateProductCommand(
                organizationId, ProductType.Goods, "Helmet", category.Id, unit.Id, null, true, 200m, 120m,
                VatRate.NoVat, 0, true),
            CancellationToken.None);
        var service = await new CreateProductCommandHandler(db, numberGenerator).Handle(
            new CreateProductCommand(
                organizationId, ProductType.Service, "Consulting", category.Id, unit.Id, null, true, 900m, 900m,
                VatRate.NoVat, 0, false),
            CancellationToken.None);

        var assetGroup = await new CreateAccountGroupCommandHandler(db).Handle(
            new CreateAccountGroupCommand(organizationId, "Current Assets", AccountRootType.Asset, null), CancellationToken.None);
        var liabilityGroup = await new CreateAccountGroupCommandHandler(db).Handle(
            new CreateAccountGroupCommand(organizationId, "Current Liabilities", AccountRootType.Liability, null), CancellationToken.None);
        var expenseGroup = await new CreateAccountGroupCommandHandler(db).Handle(
            new CreateAccountGroupCommand(organizationId, "Operating Expenses", AccountRootType.Expense, null), CancellationToken.None);

        var ap = await new CreateAccountCommandHandler(db, numberGenerator).Handle(
            new CreateAccountCommand(organizationId, "Accounts Payable", liabilityGroup.Id), CancellationToken.None);
        var purchase = await new CreateAccountCommandHandler(db, numberGenerator).Handle(
            new CreateAccountCommand(organizationId, "Purchase Expense", expenseGroup.Id), CancellationToken.None);
        var inventory = await new CreateAccountCommandHandler(db, numberGenerator).Handle(
            new CreateAccountCommand(organizationId, "Inventory", assetGroup.Id), CancellationToken.None);
        var cogs = await new CreateAccountCommandHandler(db, numberGenerator).Handle(
            new CreateAccountCommand(organizationId, "Cost of Goods Sold", expenseGroup.Id), CancellationToken.None);
        var clearing = await new CreateAccountCommandHandler(db, numberGenerator).Handle(
            new CreateAccountCommand(organizationId, "Landed Cost Clearing", liabilityGroup.Id), CancellationToken.None);

        var freight = await new CreateCostTermCommandHandler(db).Handle(
            new CreateCostTermCommand(organizationId, "Freight", CostTermCategory.AdditionalCost), CancellationToken.None);
        var labour = await new CreateCostTermCommandHandler(db).Handle(
            new CreateCostTermCommand(organizationId, "Labour", CostTermCategory.ProductionCost), CancellationToken.None);

        var settings = TenantSettings.CreateDefault(organizationId);
        settings.SetAccountingDefaults(null, null, null, purchase.Id, ap.Id, null, null);
        settings.SetInventoryDefaults(inventory.Id, cogs.Id, null, null, withClearingAccount ? clearing.Id : null);
        db.TenantSettings.Add(settings);
        await db.SaveChangesAsync(CancellationToken.None);

        return new Seed(
            organizationId, numberGenerator, supplier.Id, warehouse.Id, goodsA.Id, goodsB.Id, service.Id,
            inventory.Id, ap.Id, clearing.Id, freight.Id, labour.Id);
    }
}
