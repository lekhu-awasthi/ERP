using ErpApp.Application.Accounting.Commands.CreateAccount;
using ErpApp.Application.Accounting.Commands.CreateAccountGroup;
using ErpApp.Application.Catalog.Commands.CreateProduct;
using ErpApp.Application.Catalog.Commands.CreateProductCategory;
using ErpApp.Application.Catalog.Commands.CreateUnitOfMeasurement;
using ErpApp.Application.Common.Persistence;
using ErpApp.Application.Configuration.Commands.CreateCostTerm;
using ErpApp.Application.Contacts.Commands.CreateContact;
using ErpApp.Application.Inventory.Stock;
using ErpApp.Application.Purchasing;
using ErpApp.Application.Purchasing.Commands.ApprovePurchaseBill;
using ErpApp.Application.Purchasing.Commands.CreatePurchaseBill;
using ErpApp.Application.Purchasing.Posting;
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

namespace ErpApp.Application.UnitTests.Manufacturing;

/// <summary>
/// Shared fixture for the manufacturing handler suites. Stock is seeded by <b>really approving a
/// PurchaseBill</b> rather than by inserting StockLedgerEntry rows directly, so the FIFO layers
/// under test were created by the same engine production consumes them with -- phase-8b's lesson
/// about seeding through real handlers.
/// </summary>
internal sealed record ManufacturingSeed(
    Guid OrganizationId,
    FakeDocumentNumberGenerator NumberGenerator,
    Guid SupplierId,
    Guid WarehouseId,
    Guid OtherWarehouseId,
    Guid FinishedProductId,
    Guid RawProductId,
    Guid SecondRawProductId,
    Guid ByProductId,
    Guid ServiceProductId,
    Guid CostTermId,
    Guid AdditionalCostTermId,
    Guid InventoryAccountId,
    Guid ProductionCostAccountId,
    Guid PurchaseAccountId,
    Guid AccountsPayableId);

internal static class ManufacturingTestSeed
{
    public static async Task<ManufacturingSeed> CreateAsync(IAppDbContext db, BalanceAction negativeStock = BalanceAction.Warn)
    {
        var organizationId = Guid.NewGuid();
        var numbers = new FakeDocumentNumberGenerator();

        // Written first, and saved: CreateWarehouseCommandHandler reads the subscription to
        // enforce the MultipleWarehouses cap, so a seed that adds it later cannot create a second
        // warehouse.
        db.TenantSubscriptions.Add(TenantSubscription.CreateTrial(
            organizationId,
            new AccountingFeatureSelections(
                TrackInventory: true, MultipleLocations: false, MultipleWarehouses: true, MultiCurrency: false,
                Manufacturing: true, PosRetail: false, PosRestaurant: false)));
        await db.SaveChangesAsync(CancellationToken.None);

        var supplier = await new CreateContactCommandHandler(db, numbers).Handle(
            new CreateContactCommand(organizationId, ContactType.Supplier, "Raw Supplies", null, null, null, null, null, 0m),
            CancellationToken.None);

        var warehouse = await new CreateWarehouseCommandHandler(db).Handle(
            new CreateWarehouseCommand(organizationId, "Main Warehouse"), CancellationToken.None);
        var otherWarehouse = await new CreateWarehouseCommandHandler(db).Handle(
            new CreateWarehouseCommand(organizationId, "Second Warehouse"), CancellationToken.None);

        var category = await new CreateProductCategoryCommandHandler(db).Handle(
            new CreateProductCategoryCommand(organizationId, "General", null), CancellationToken.None);
        var unit = await new CreateUnitOfMeasurementCommandHandler(db).Handle(
            new CreateUnitOfMeasurementCommand(organizationId, "Piece", "pc"), CancellationToken.None);

        async Task<Guid> ProductAsync(string name, ProductType type = ProductType.Goods)
        {
            var created = await new CreateProductCommandHandler(db, numbers).Handle(
                new CreateProductCommand(
                    organizationId, type, name, category.Id, unit.Id, null, true, 150m, 100m, VatRate.NoVat, 0,
                    type == ProductType.Goods),
                CancellationToken.None);
            return created.Id;
        }

        var finished = await ProductAsync("Finished Widget");
        var raw = await ProductAsync("Steel Sheet");
        var secondRaw = await ProductAsync("Bolt");
        var byProduct = await ProductAsync("Steel Offcut");
        var service = await ProductAsync("Consulting", ProductType.Service);

        var costTerm = await new CreateCostTermCommandHandler(db).Handle(
            new CreateCostTermCommand(organizationId, "Direct Labor Costs", CostTermCategory.ProductionCost),
            CancellationToken.None);
        var additionalCostTerm = await new CreateCostTermCommandHandler(db).Handle(
            new CreateCostTermCommand(organizationId, "Freight", CostTermCategory.AdditionalCost),
            CancellationToken.None);

        var assetGroup = await new CreateAccountGroupCommandHandler(db).Handle(
            new CreateAccountGroupCommand(organizationId, "Current Assets", AccountRootType.Asset, null), CancellationToken.None);
        var liabilityGroup = await new CreateAccountGroupCommandHandler(db).Handle(
            new CreateAccountGroupCommand(organizationId, "Current Liabilities", AccountRootType.Liability, null), CancellationToken.None);
        var expenseGroup = await new CreateAccountGroupCommandHandler(db).Handle(
            new CreateAccountGroupCommand(organizationId, "Operating Expenses", AccountRootType.Expense, null), CancellationToken.None);

        async Task<Guid> AccountAsync(string name, Guid groupId)
        {
            var created = await new CreateAccountCommandHandler(db, numbers).Handle(
                new CreateAccountCommand(organizationId, name, groupId), CancellationToken.None);
            return created.Id;
        }

        var inventory = await AccountAsync("Inventory", assetGroup.Id);
        var productionCost = await AccountAsync("Production Cost Applied", expenseGroup.Id);
        var purchase = await AccountAsync("Purchase Expense", expenseGroup.Id);
        var cogs = await AccountAsync("Cost of Goods Sold", expenseGroup.Id);
        var ap = await AccountAsync("Accounts Payable", liabilityGroup.Id);

        var settings = TenantSettings.CreateDefault(organizationId);
        settings.SetAccountingDefaults(null, null, null, purchase, ap, null, null);
        settings.SetInventoryDefaults(inventory, cogs, null, productionCost);
        settings.UpdateSettings(
            SuggestSellingPriceMode.RecentSellingPrice, ProductPriceBasis.ExclusiveOfVat,
            InventoryTrackingMode.AccountingMovement, BalanceAction.Reject, negativeStock);
        db.TenantSettings.Add(settings);

        await db.SaveChangesAsync(CancellationToken.None);

        return new ManufacturingSeed(
            organizationId, numbers, supplier.Id, warehouse.Id, otherWarehouse.Id, finished, raw, secondRaw,
            byProduct, service, costTerm.Id, additionalCostTerm.Id, inventory, productionCost, purchase, ap);
    }

    /// <summary>Puts a real FIFO layer in stock by creating and approving a PurchaseBill, so the
    /// layer under test was made by the engine production will consume it with.</summary>
    public static async Task ReceiveStockAsync(
        IAppDbContext db, ManufacturingSeed seed, Guid productId, decimal quantity, decimal rate, DateOnly date,
        Guid? warehouseId = null)
    {
        var created = await new CreatePurchaseBillCommandHandler(db).Handle(
            new CreatePurchaseBillCommand(
                seed.OrganizationId, seed.SupplierId, warehouseId ?? seed.WarehouseId, date, null, null, false,
                null, null, null, null,
                [new PurchaseBillLineInput(productId, quantity, rate, VatRate.NoVat, ExpenditureClassification.Others)]),
            CancellationToken.None);

        await new ApprovePurchaseBillCommandHandler(
            db, seed.NumberGenerator, new FakeCurrentUserService(Guid.NewGuid()), new PurchaseBillPostingRule(),
            new StockLedgerService(db))
            .Handle(new ApprovePurchaseBillCommand(seed.OrganizationId, created.Id), CancellationToken.None);
    }

    public static async Task<List<GlLine>> GlLinesForAsync(
        IAppDbContext db, Guid organizationId, DocumentType sourceType, Guid sourceId)
    {
        var entries = await db.GlJournalEntries.Include(x => x.Lines)
            .Where(x => x.OrganizationId == organizationId
                && x.SourceDocumentType == sourceType && x.SourceDocumentId == sourceId)
            .ToListAsync();

        return entries.SelectMany(x => x.Lines).ToList();
    }

    /// <summary>Net movement on one account across every entry posted for a document -- a debit
    /// minus credit figure, which is what phase-6 bug #3 says to assert rather than trusting that
    /// each entry balances on its own.</summary>
    public static decimal NetMovement(IEnumerable<GlLine> lines, Guid accountId) =>
        lines.Where(x => x.AccountId == accountId).Sum(x => x.Debit - x.Credit);
}
