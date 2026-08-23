using ErpApp.Application.Accounting;
using ErpApp.Application.Accounting.Commands.CreateAccount;
using ErpApp.Application.Accounting.Commands.CreateAccountGroup;
using ErpApp.Application.Catalog.Commands.CreateProduct;
using ErpApp.Application.Catalog.Commands.CreateProductCategory;
using ErpApp.Application.Catalog.Commands.CreateUnitOfMeasurement;
using ErpApp.Application.Common.Persistence;
using ErpApp.Application.Contacts.Commands.CreateContact;
using ErpApp.Application.Inventory.Queries.ProductStockPosition;
using ErpApp.Application.Inventory.Queries.StockAgeing;
using ErpApp.Application.Inventory.Stock;
using ErpApp.Application.Purchasing;
using ErpApp.Application.Purchasing.Commands.ApprovePurchaseBill;
using ErpApp.Application.Purchasing.Commands.CreatePurchaseBill;
using ErpApp.Application.Purchasing.Posting;
using ErpApp.Application.Tenancy.Commands.CreateWarehouse;
using ErpApp.Application.UnitTests.TestSupport;
using ErpApp.Domain.Accounting;
using ErpApp.Domain.Catalog;
using ErpApp.Domain.Contacts;
using ErpApp.Domain.Purchasing;
using ErpApp.Domain.Tenancy;

namespace ErpApp.Application.UnitTests.Inventory;

public class StockAgeingQueryHandlerTests
{
    /// <summary>Phase 19 decision #4 -- same bucket boundaries as
    /// ContactAgeingSummaryQueryHandler. Exit criterion #5 -- bucket totals must reconcile exactly
    /// against ProductStockPositionQuery's Balance for the same product/warehouse/as-of-date.</summary>
    [Fact]
    public async Task Handle_buckets_by_age_and_reconciles_against_stock_position_balance()
    {
        var db = TestAppDbContext.Create();
        var seed = await SeedAsync(db);
        var asOf = new DateOnly(2026, 5, 1);

        await CreateAndApprovePurchaseBillAsync(db, seed, asOf.AddDays(-30), 10m, 50m); // age 30 -> 1-30, cost 500
        await CreateAndApprovePurchaseBillAsync(db, seed, asOf.AddDays(-61), 5m, 40m); // age 61 -> 61-90, cost 200

        var handler = new StockAgeingQueryHandler(db);
        var result = await handler.Handle(
            new StockAgeingQuery(seed.OrganizationId, asOf, null, null, null), CancellationToken.None);

        var row = Assert.Single(result.Items);
        Assert.Equal(10m, row.Days1To30);
        Assert.Equal(0m, row.Days31To60);
        Assert.Equal(5m, row.Days61To90);
        Assert.Equal(0m, row.Days91Plus);
        Assert.Equal(15m, row.Total);
        Assert.Equal(700m, row.Amount); // 10*50 + 5*40

        var stockPosition = await new ProductStockPositionQueryHandler(db).Handle(
            new ProductStockPositionQuery(seed.OrganizationId, seed.ProductId, seed.WarehouseId), CancellationToken.None);
        var position = Assert.Single(stockPosition);
        Assert.Equal(row.Total, position.Balance);
    }

    [Fact]
    public async Task Handle_excludes_layers_created_after_the_as_of_date()
    {
        var db = TestAppDbContext.Create();
        var seed = await SeedAsync(db);
        var asOf = new DateOnly(2026, 5, 1);

        await CreateAndApprovePurchaseBillAsync(db, seed, asOf.AddDays(-10), 10m, 50m);
        await CreateAndApprovePurchaseBillAsync(db, seed, asOf.AddDays(10), 999m, 1m); // future -- excluded

        var handler = new StockAgeingQueryHandler(db);
        var result = await handler.Handle(
            new StockAgeingQuery(seed.OrganizationId, asOf, null, null, null), CancellationToken.None);

        var row = Assert.Single(result.Items);
        Assert.Equal(10m, row.Total);
    }

    private sealed record Seed(
        Guid OrganizationId, FakeDocumentNumberGenerator NumberGenerator, Guid SupplierId, Guid WarehouseId, Guid ProductId);

    private static async Task<Seed> SeedAsync(IAppDbContext db)
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
        var product = await new CreateProductCommandHandler(db, numberGenerator).Handle(
            new CreateProductCommand(
                organizationId, ProductType.Goods, "Widget", category.Id, unit.Id, null, true, 150m, 100m,
                VatRate.NoVat, 0, true),
            CancellationToken.None);

        var assetGroup = await new CreateAccountGroupCommandHandler(db).Handle(
            new CreateAccountGroupCommand(organizationId, "Current Assets", AccountRootType.Asset, null), CancellationToken.None);
        var liabilityGroup = await new CreateAccountGroupCommandHandler(db).Handle(
            new CreateAccountGroupCommand(organizationId, "Current Liabilities", AccountRootType.Liability, null), CancellationToken.None);
        var expenseGroup = await new CreateAccountGroupCommandHandler(db).Handle(
            new CreateAccountGroupCommand(organizationId, "Operating Expenses", AccountRootType.Expense, null), CancellationToken.None);

        var vatReceivable = await new CreateAccountCommandHandler(db, numberGenerator).Handle(
            new CreateAccountCommand(organizationId, "VAT Receivable", assetGroup.Id), CancellationToken.None);
        var ap = await new CreateAccountCommandHandler(db, numberGenerator).Handle(
            new CreateAccountCommand(organizationId, "Accounts Payable", liabilityGroup.Id), CancellationToken.None);
        var purchase = await new CreateAccountCommandHandler(db, numberGenerator).Handle(
            new CreateAccountCommand(organizationId, "Purchase Expense", expenseGroup.Id), CancellationToken.None);
        var inventory = await new CreateAccountCommandHandler(db, numberGenerator).Handle(
            new CreateAccountCommand(organizationId, "Inventory", assetGroup.Id), CancellationToken.None);
        var cogs = await new CreateAccountCommandHandler(db, numberGenerator).Handle(
            new CreateAccountCommand(organizationId, "Cost of Goods Sold", expenseGroup.Id), CancellationToken.None);

        var settings = TenantSettings.CreateDefault(organizationId);
        settings.SetAccountingDefaults(null, null, null, purchase.Id, ap.Id, vatReceivable.Id, null);
        settings.SetInventoryDefaults(inventory.Id, cogs.Id, null);
        db.TenantSettings.Add(settings);
        await db.SaveChangesAsync(CancellationToken.None);

        return new Seed(organizationId, numberGenerator, supplier.Id, warehouse.Id, product.Id);
    }

    private static async Task<(Guid Id, string Code)> CreateAndApprovePurchaseBillAsync(
        IAppDbContext db, Seed seed, DateOnly date, decimal quantity, decimal rate)
    {
        var created = await new CreatePurchaseBillCommandHandler(db).Handle(
            new CreatePurchaseBillCommand(
                seed.OrganizationId, seed.SupplierId, seed.WarehouseId, date, null, null, false, null, null, null, null,
                [new PurchaseBillLineInput(seed.ProductId, quantity, rate, VatRate.NoVat, ExpenditureClassification.Others)]),
            CancellationToken.None);

        var approved = await new ApprovePurchaseBillCommandHandler(
            db, seed.NumberGenerator, new FakeCurrentUserService(Guid.NewGuid()), new PurchaseBillPostingRule(),
            new StockLedgerService(db))
            .Handle(new ApprovePurchaseBillCommand(seed.OrganizationId, created.Id), CancellationToken.None);

        return (approved.Id, approved.Code);
    }
}
