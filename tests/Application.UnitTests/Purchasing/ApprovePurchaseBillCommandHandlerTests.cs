using ErpApp.Application.Accounting;
using ErpApp.Application.Accounting.Commands.CreateAccount;
using ErpApp.Application.Accounting.Commands.CreateAccountGroup;
using ErpApp.Application.Catalog.Commands.CreateProduct;
using ErpApp.Application.Catalog.Commands.CreateProductCategory;
using ErpApp.Application.Catalog.Commands.CreateUnitOfMeasurement;
using ErpApp.Application.Catalog.Commands.UpdateProduct;
using ErpApp.Application.Common.Persistence;
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
using ErpApp.Domain.Contacts;
using ErpApp.Domain.Purchasing;
using ErpApp.Domain.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.UnitTests.Purchasing;

/// <summary>
/// Regression coverage for the post-Phase-19 fix (see PurchaseBillAccountResolver's doc comment):
/// a Goods line must debit the Inventory asset account, not Purchase Expense, so its cost isn't
/// double-counted once as a period expense here and again as COGS on the eventual Invoice's FIFO
/// relief. A Service line's behaviour is unchanged from Phase 6.
/// </summary>
public class ApprovePurchaseBillCommandHandlerTests
{
    [Fact]
    public async Task Handle_debits_inventory_account_for_a_goods_line_not_purchase_expense()
    {
        var db = TestAppDbContext.Create();
        var seed = await SeedAsync(db);

        var (id, _) = await CreateAndApprovePurchaseBillAsync(db, seed, seed.GoodsProductId, 10m, 40m);

        var glLines = await GlLinesForAsync(db, seed.OrganizationId, DocumentType.PurchaseBill, id);
        Assert.Contains(glLines, l => l.AccountId == seed.InventoryAccountId && l.Debit == 400m);
        Assert.DoesNotContain(glLines, l => l.AccountId == seed.PurchaseAccountId);
    }

    [Fact]
    public async Task Handle_debits_purchase_expense_for_a_service_line()
    {
        var db = TestAppDbContext.Create();
        var seed = await SeedAsync(db);

        var (id, _) = await CreateAndApprovePurchaseBillAsync(db, seed, seed.ServiceProductId, 2m, 100m);

        var glLines = await GlLinesForAsync(db, seed.OrganizationId, DocumentType.PurchaseBill, id);
        Assert.Contains(glLines, l => l.AccountId == seed.PurchaseAccountId && l.Debit == 200m);
        Assert.DoesNotContain(glLines, l => l.AccountId == seed.InventoryAccountId);
    }

    [Fact]
    public async Task Handle_ignores_a_goods_products_own_purchase_account_override()
    {
        var db = TestAppDbContext.Create();
        var seed = await SeedAsync(db);

        var product = await db.Products.SingleAsync(x => x.Id == seed.GoodsProductId);
        await new UpdateProductCommandHandler(db).Handle(
            new UpdateProductCommand(
                seed.OrganizationId, product.Id, product.Name, product.CategoryId, product.PrimaryUnitId, null,
                product.AvailableForSale, product.SellingPrice, product.PurchasePrice, product.VatRate,
                product.ReOrderLevel, product.TrackInventory, product.IsActive,
                PurchaseAccountId: seed.PurchaseAccountId),
            CancellationToken.None);

        var (id, _) = await CreateAndApprovePurchaseBillAsync(db, seed, seed.GoodsProductId, 5m, 40m);

        var glLines = await GlLinesForAsync(db, seed.OrganizationId, DocumentType.PurchaseBill, id);
        Assert.Contains(glLines, l => l.AccountId == seed.InventoryAccountId && l.Debit == 200m);
        Assert.DoesNotContain(glLines, l => l.AccountId == seed.PurchaseAccountId);
    }

    private static async Task<List<GlLine>> GlLinesForAsync(IAppDbContext db, Guid organizationId, DocumentType sourceType, Guid sourceId)
    {
        var entry = await db.GlJournalEntries.Include(x => x.Lines).SingleAsync(
            x => x.OrganizationId == organizationId && x.SourceDocumentType == sourceType && x.SourceDocumentId == sourceId);
        return entry.Lines.ToList();
    }

    private sealed record Seed(
        Guid OrganizationId, FakeDocumentNumberGenerator NumberGenerator, Guid SupplierId, Guid WarehouseId,
        Guid GoodsProductId, Guid ServiceProductId, Guid PurchaseAccountId, Guid InventoryAccountId, Guid AccountsPayableId);

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

        var goodsProduct = await new CreateProductCommandHandler(db, numberGenerator).Handle(
            new CreateProductCommand(
                organizationId, ProductType.Goods, "Widget", category.Id, unit.Id, null, true, 150m, 100m,
                VatRate.NoVat, 0, true),
            CancellationToken.None);
        var serviceProduct = await new CreateProductCommandHandler(db, numberGenerator).Handle(
            new CreateProductCommand(
                organizationId, ProductType.Service, "Consulting", category.Id, unit.Id, null, true, 150m, 100m,
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

        var settings = TenantSettings.CreateDefault(organizationId);
        settings.SetAccountingDefaults(null, null, null, purchase.Id, ap.Id, null, null);
        settings.SetInventoryDefaults(inventory.Id, cogs.Id, null, null);
        db.TenantSettings.Add(settings);
        await db.SaveChangesAsync(CancellationToken.None);

        return new Seed(
            organizationId, numberGenerator, supplier.Id, warehouse.Id, goodsProduct.Id, serviceProduct.Id,
            purchase.Id, inventory.Id, ap.Id);
    }

    private static async Task<(Guid Id, string Code)> CreateAndApprovePurchaseBillAsync(
        IAppDbContext db, Seed seed, Guid productId, decimal quantity, decimal rate)
    {
        var created = await new CreatePurchaseBillCommandHandler(db).Handle(
            new CreatePurchaseBillCommand(
                seed.OrganizationId, seed.SupplierId, seed.WarehouseId, new DateOnly(2026, 1, 10), null, null, false,
                null, null, null, null,
                [new PurchaseBillLineInput(productId, quantity, rate, VatRate.NoVat, ExpenditureClassification.Others)]),
            CancellationToken.None);

        var approved = await new ApprovePurchaseBillCommandHandler(
            db, seed.NumberGenerator, new FakeCurrentUserService(Guid.NewGuid()), new PurchaseBillPostingRule(),
            new StockLedgerService(db))
            .Handle(new ApprovePurchaseBillCommand(seed.OrganizationId, created.Id), CancellationToken.None);

        return (approved.Id, approved.Code);
    }
}
