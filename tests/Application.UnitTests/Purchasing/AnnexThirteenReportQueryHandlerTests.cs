using ErpApp.Application.Accounting;
using ErpApp.Application.Accounting.Commands.CreateAccount;
using ErpApp.Application.Accounting.Commands.CreateAccountGroup;
using ErpApp.Application.Catalog.Commands.CreateProduct;
using ErpApp.Application.Catalog.Commands.CreateProductCategory;
using ErpApp.Application.Catalog.Commands.CreateUnitOfMeasurement;
using ErpApp.Application.Common.Persistence;
using ErpApp.Application.Contacts.Commands.CreateContact;
using ErpApp.Application.Inventory.Stock;
using ErpApp.Application.Purchasing;
using ErpApp.Application.Purchasing.Commands.ApproveDebitNote;
using ErpApp.Application.Purchasing.Commands.ApproveExpense;
using ErpApp.Application.Purchasing.Commands.ApprovePurchaseBill;
using ErpApp.Application.Purchasing.Commands.CreateDebitNote;
using ErpApp.Application.Purchasing.Commands.CreateExpense;
using ErpApp.Application.Purchasing.Commands.CreatePurchaseBill;
using ErpApp.Application.Purchasing.Posting;
using ErpApp.Application.Purchasing.Queries.AnnexThirteenReport;
using ErpApp.Application.Sales;
using ErpApp.Application.Sales.Commands.ApproveCreditNote;
using ErpApp.Application.Sales.Commands.ApproveInvoice;
using ErpApp.Application.Sales.Commands.CreateCreditNote;
using ErpApp.Application.Sales.Commands.CreateInvoice;
using ErpApp.Application.Sales.Posting;
using ErpApp.Application.Sales.Stock;
using ErpApp.Application.Tenancy.Commands.CreateWarehouse;
using ErpApp.Application.UnitTests.TestSupport;
using ErpApp.Domain.Accounting;
using ErpApp.Domain.Catalog;
using ErpApp.Domain.Common;
using ErpApp.Domain.Contacts;
using ErpApp.Domain.Purchasing;
using ErpApp.Domain.Tenancy;

namespace ErpApp.Application.UnitTests.Purchasing;

public class AnnexThirteenReportQueryHandlerTests
{
    [Fact]
    public async Task Handle_buckets_activity_by_product_type_and_expenditure_classification_including_a_debit_note_reversal_and_an_expense()
    {
        var db = TestAppDbContext.Create();
        var seed = await SeedAsync(db);

        // Purchase side: one PurchaseBill with four lines, one per bucket -- Goods/Service x
        // Capital/Others, each at a distinct Rate so they don't collide on the (ProductId, Rate,
        // VatRate) key the DebitNote-classification lookup keys on.
        var bill = await CreateAndApprovePurchaseBillAsync(
            db, seed, new DateOnly(2026, 1, 10),
            [
                (seed.GoodsProductId, 5m, 100m, ExpenditureClassification.Capital), // 500 -> GoodsPurchaseCapital
                (seed.GoodsProductId, 3m, 50m, ExpenditureClassification.Others), // 150 -> GoodsPurchaseOthers
                (seed.ServiceProductId, 2m, 100m, ExpenditureClassification.Capital), // 200 -> ServicePurchaseCapital
                (seed.ServiceProductId, 1m, 50m, ExpenditureClassification.Others), // 50 -> ServicePurchaseOthers
            ]);

        // DebitNote reverses 2 of the 5 Capital-classified Goods units (same ProductId/Rate/VatRate
        // as that line) -- must be resolved back to Capital via the source line, not default to
        // Others, and must subtract from GoodsPurchaseCapital specifically.
        await CreateAndApproveDebitNoteAsync(
            db, seed, new DateOnly(2026, 1, 15), seed.GoodsProductId, 2m, 100m, bill.Id); // -200 -> GoodsPurchaseCapital

        // Expense carries no ProductId/ExpenditureClassification -- bucketed as ServicePurchaseOthers.
        await CreateAndApproveExpenseAsync(db, seed, new DateOnly(2026, 1, 20), 80m); // +80 -> ServicePurchaseOthers

        // Sales side: one Invoice with a Goods line and a Service line, netted against a CreditNote
        // reversing part of the Goods line.
        await CreateAndApproveInvoiceAsync(
            db, seed, new DateOnly(2026, 1, 12),
            [(seed.GoodsProductId, 4m, 100m), (seed.ServiceProductId, 3m, 100m)]); // 400 Goods, 300 Service
        await CreateAndApproveCreditNoteAsync(db, seed, new DateOnly(2026, 1, 16), seed.GoodsProductId, 1m, 100m); // -100 Goods

        var handler = new AnnexThirteenReportQueryHandler(db);
        var result = await handler.Handle(
            new AnnexThirteenReportQuery(seed.OrganizationId, new DateOnly(2026, 1, 1), new DateOnly(2026, 1, 31), ThresholdAmount: 0m),
            CancellationToken.None);

        var supplierRow = Assert.Single(result.Rows, r => r.ContactId == seed.SupplierId);
        Assert.Equal(ContactType.Supplier, supplierRow.ContactType);
        Assert.Equal(200m, supplierRow.ServicePurchaseCapital); // unaffected by the Goods-side reversal
        Assert.Equal(130m, supplierRow.ServicePurchaseOthers); // 50 (line) + 80 (Expense)
        Assert.Equal(300m, supplierRow.GoodsPurchaseCapital); // 500 - 200 (DebitNote reversal)
        Assert.Equal(150m, supplierRow.GoodsPurchaseOthers);
        Assert.Equal(0m, supplierRow.ServiceSales);
        Assert.Equal(0m, supplierRow.GoodsSales);

        var customerRow = Assert.Single(result.Rows, r => r.ContactId == seed.CustomerId);
        Assert.Equal(ContactType.Customer, customerRow.ContactType);
        Assert.Equal(300m, customerRow.ServiceSales);
        Assert.Equal(300m, customerRow.GoodsSales); // 400 - 100 (CreditNote reversal)
        Assert.Equal(0m, customerRow.ServicePurchaseCapital + customerRow.ServicePurchaseOthers
            + customerRow.GoodsPurchaseCapital + customerRow.GoodsPurchaseOthers);

        // Closing Balance = OpeningBalance + this row's own net period activity (the six buckets
        // summed) -- not re-derived from a ledger, and explicitly not including Payment allocations.
        Assert.Equal(780m, supplierRow.TotalActivity); // 200+130+300+150
        Assert.Equal(1000m + 780m, supplierRow.ClosingBalance);
        Assert.Equal(600m, customerRow.TotalActivity); // 300+300
        Assert.Equal(0m + 600m, customerRow.ClosingBalance);
    }

    [Fact]
    public async Task Handle_filters_by_threshold_approved_only_status_and_date_range()
    {
        var db = TestAppDbContext.Create();
        var seed = await SeedAsync(db);

        // A second supplier whose only activity lands exactly at the threshold -- included.
        var atThresholdSupplier = await CreateSupplierAsync(db, seed, "At Threshold Supplier");
        await CreateAndApprovePurchaseBillAsync(
            db, seed, atThresholdSupplier, new DateOnly(2026, 1, 10),
            [(seed.ServiceProductId, 10m, 100m, ExpenditureClassification.Others)]); // exactly 1000

        // A third supplier whose only activity lands just under the threshold -- excluded entirely,
        // not shown as a zero/small row.
        var underThresholdSupplier = await CreateSupplierAsync(db, seed, "Under Threshold Supplier");
        await CreateAndApprovePurchaseBillAsync(
            db, seed, underThresholdSupplier, new DateOnly(2026, 1, 10),
            [(seed.ServiceProductId, 9m, 99m, ExpenditureClassification.Others)]); // 891, just under 1000

        // Draft PurchaseBill on the main Supplier -- must be excluded, Approved-only.
        await CreatePurchaseBillAsync(
            db, seed, seed.SupplierId, new DateOnly(2026, 1, 11),
            [(seed.ServiceProductId, 50m, 100m, ExpenditureClassification.Others)]); // would be 5000 if counted

        // Approved PurchaseBill on the main Supplier but outside the query's date range -- excluded.
        await CreateAndApprovePurchaseBillAsync(
            db, seed, seed.SupplierId, new DateOnly(2026, 3, 1),
            [(seed.ServiceProductId, 50m, 100m, ExpenditureClassification.Others)]);

        var handler = new AnnexThirteenReportQueryHandler(db);
        var result = await handler.Handle(
            new AnnexThirteenReportQuery(seed.OrganizationId, new DateOnly(2026, 1, 1), new DateOnly(2026, 1, 31), ThresholdAmount: 1000m),
            CancellationToken.None);

        // The main Supplier has no in-range Approved activity at all this test (Draft/out-of-range
        // only), so it's absent; only the exactly-at-threshold supplier survives the filter.
        var row = Assert.Single(result.Rows);
        Assert.Equal(atThresholdSupplier, row.ContactId);
        Assert.Equal(1000m, row.TotalActivity);
    }

    // See PurchaseMasterReportQueryHandlerTests' matching comment: a single FakeDocumentNumberGenerator
    // instance is shared across every Create/Approve call in one test, or every approved document
    // collides on the same "Foo-0001" code.
    private sealed record Seed(
        Guid OrganizationId, FakeDocumentNumberGenerator NumberGenerator, Guid CustomerId, Guid SupplierId,
        Guid WarehouseId, Guid GoodsProductId, Guid ServiceProductId, Guid ExpenseAccountId, Guid CategoryId, Guid UnitId);

    private static async Task<Seed> SeedAsync(IAppDbContext db)
    {
        var organizationId = Guid.NewGuid();
        var numberGenerator = new FakeDocumentNumberGenerator();

        var customer = await new CreateContactCommandHandler(db, numberGenerator).Handle(
            new CreateContactCommand(organizationId, ContactType.Customer, "Acme Traders", null, null, null, null, null, 0m),
            CancellationToken.None);
        // Supplier seeded with an OpeningBalance to exercise ClosingBalance's OpeningBalance + net
        // period activity formula.
        var supplier = await new CreateContactCommandHandler(db, numberGenerator).Handle(
            new CreateContactCommand(
                organizationId, ContactType.Supplier, "Global Supplies", null, "301234567", null, null, null, 1000m),
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
        var incomeGroup = await new CreateAccountGroupCommandHandler(db).Handle(
            new CreateAccountGroupCommand(organizationId, "Sales Income", AccountRootType.Income, null), CancellationToken.None);
        var expenseGroup = await new CreateAccountGroupCommandHandler(db).Handle(
            new CreateAccountGroupCommand(organizationId, "Operating Expenses", AccountRootType.Expense, null), CancellationToken.None);

        var ar = await new CreateAccountCommandHandler(db, numberGenerator).Handle(
            new CreateAccountCommand(organizationId, "Accounts Receivable", assetGroup.Id), CancellationToken.None);
        var vatPayable = await new CreateAccountCommandHandler(db, numberGenerator).Handle(
            new CreateAccountCommand(organizationId, "VAT Payable", liabilityGroup.Id), CancellationToken.None);
        var sales = await new CreateAccountCommandHandler(db, numberGenerator).Handle(
            new CreateAccountCommand(organizationId, "Sales Revenue", incomeGroup.Id), CancellationToken.None);
        var vatReceivable = await new CreateAccountCommandHandler(db, numberGenerator).Handle(
            new CreateAccountCommand(organizationId, "VAT Receivable", assetGroup.Id), CancellationToken.None);
        var ap = await new CreateAccountCommandHandler(db, numberGenerator).Handle(
            new CreateAccountCommand(organizationId, "Accounts Payable", liabilityGroup.Id), CancellationToken.None);
        var purchase = await new CreateAccountCommandHandler(db, numberGenerator).Handle(
            new CreateAccountCommand(organizationId, "Purchase Expense", expenseGroup.Id), CancellationToken.None);
        var officeExpense = await new CreateAccountCommandHandler(db, numberGenerator).Handle(
            new CreateAccountCommand(organizationId, "Office Expense", expenseGroup.Id), CancellationToken.None);
        var inventory = await new CreateAccountCommandHandler(db, numberGenerator).Handle(
            new CreateAccountCommand(organizationId, "Inventory", assetGroup.Id), CancellationToken.None);
        var cogs = await new CreateAccountCommandHandler(db, numberGenerator).Handle(
            new CreateAccountCommand(organizationId, "Cost of Goods Sold", expenseGroup.Id), CancellationToken.None);

        var settings = TenantSettings.CreateDefault(organizationId);
        settings.SetAccountingDefaults(sales.Id, ar.Id, vatPayable.Id, purchase.Id, ap.Id, vatReceivable.Id, null);
        settings.SetInventoryDefaults(inventory.Id, cogs.Id, null, null);
        db.TenantSettings.Add(settings);
        await db.SaveChangesAsync(CancellationToken.None);

        return new Seed(
            organizationId, numberGenerator, customer.Id, supplier.Id, warehouse.Id,
            goodsProduct.Id, serviceProduct.Id, officeExpense.Id, category.Id, unit.Id);
    }

    private static async Task<Guid> CreateSupplierAsync(IAppDbContext db, Seed seed, string name)
    {
        var supplier = await new CreateContactCommandHandler(db, seed.NumberGenerator).Handle(
            new CreateContactCommand(seed.OrganizationId, ContactType.Supplier, name, null, null, null, null, null, 0m),
            CancellationToken.None);
        return supplier.Id;
    }

    private static async Task<CreatePurchaseBillResult> CreatePurchaseBillAsync(
        IAppDbContext db, Seed seed, Guid supplierId, DateOnly date,
        IReadOnlyList<(Guid ProductId, decimal Quantity, decimal Rate, ExpenditureClassification Classification)> lines)
    {
        return await new CreatePurchaseBillCommandHandler(db).Handle(
            new CreatePurchaseBillCommand(
                seed.OrganizationId, supplierId, seed.WarehouseId, date, null, null, false, null, null, null, null,
                lines.Select(l => new PurchaseBillLineInput(l.ProductId, l.Quantity, l.Rate, VatRate.NoVat, l.Classification)).ToList()),
            CancellationToken.None);
    }

    private static async Task<(Guid Id, string Code)> CreateAndApprovePurchaseBillAsync(
        IAppDbContext db, Seed seed, DateOnly date,
        IReadOnlyList<(Guid ProductId, decimal Quantity, decimal Rate, ExpenditureClassification Classification)> lines)
        => await CreateAndApprovePurchaseBillAsync(db, seed, seed.SupplierId, date, lines);

    private static async Task<(Guid Id, string Code)> CreateAndApprovePurchaseBillAsync(
        IAppDbContext db, Seed seed, Guid supplierId, DateOnly date,
        IReadOnlyList<(Guid ProductId, decimal Quantity, decimal Rate, ExpenditureClassification Classification)> lines)
    {
        var created = await CreatePurchaseBillAsync(db, seed, supplierId, date, lines);
        var approved = await new ApprovePurchaseBillCommandHandler(
            db, seed.NumberGenerator, new FakeCurrentUserService(Guid.NewGuid()), new PurchaseBillPostingRule(),
            new StockLedgerService(db))
            .Handle(new ApprovePurchaseBillCommand(seed.OrganizationId, created.Id), CancellationToken.None);
        return (approved.Id, approved.Code);
    }

    private static async Task<(Guid Id, string Code)> CreateAndApproveDebitNoteAsync(
        IAppDbContext db, Seed seed, DateOnly date, Guid productId, decimal quantity, decimal rate, Guid referrerPurchaseBillId)
    {
        var created = await new CreateDebitNoteCommandHandler(db).Handle(
            new CreateDebitNoteCommand(
                seed.OrganizationId, seed.SupplierId, date, null, null,
                [new DebitNoteLineInput(productId, quantity, rate, VatRate.NoVat)],
                DocumentType.PurchaseBill, referrerPurchaseBillId),
            CancellationToken.None);

        var approved = await new ApproveDebitNoteCommandHandler(
            db, seed.NumberGenerator, new FakeCurrentUserService(Guid.NewGuid()),
            new DebitNotePostingRule(), new StockLedgerService(db))
            .Handle(new ApproveDebitNoteCommand(seed.OrganizationId, created.Id), CancellationToken.None);

        return (approved.Id, approved.Code);
    }

    private static async Task<(Guid Id, string Code)> CreateAndApproveExpenseAsync(
        IAppDbContext db, Seed seed, DateOnly date, decimal amount)
    {
        var created = await new CreateExpenseCommandHandler(db).Handle(
            new CreateExpenseCommand(
                seed.OrganizationId, seed.SupplierId, date, null, null, null, false, null,
                [new ExpenseLineInput(seed.ExpenseAccountId, amount, VatRate.NoVat)]),
            CancellationToken.None);

        var approved = await new ApproveExpenseCommandHandler(
            db, seed.NumberGenerator, new FakeCurrentUserService(Guid.NewGuid()), new ExpensePostingRule())
            .Handle(new ApproveExpenseCommand(seed.OrganizationId, created.Id), CancellationToken.None);

        return (approved.Id, approved.Code);
    }

    private static async Task<(Guid Id, string Code)> CreateAndApproveInvoiceAsync(
        IAppDbContext db, Seed seed, DateOnly date, IReadOnlyList<(Guid ProductId, decimal Quantity, decimal Rate)> lines)
    {
        var created = await new CreateInvoiceCommandHandler(db).Handle(
            new CreateInvoiceCommand(
                seed.OrganizationId, seed.CustomerId, seed.WarehouseId, date, null,
                lines.Select(l => new InvoiceLineInput(l.ProductId, l.Quantity, l.Rate, VatRate.NoVat)).ToList()),
            CancellationToken.None);

        var stockLedgerService = new StockLedgerService(db);
        var approved = await new ApproveInvoiceCommandHandler(
            db, seed.NumberGenerator, new FakeCurrentUserService(Guid.NewGuid()), new InvoicePostingRule(),
            new FifoStockAvailabilityPolicy(db, stockLedgerService), stockLedgerService)
            .Handle(new ApproveInvoiceCommand(seed.OrganizationId, created.Id, OverrideWarning: false), CancellationToken.None);

        return (approved.Id, approved.Code);
    }

    private static async Task<(Guid Id, string Code)> CreateAndApproveCreditNoteAsync(
        IAppDbContext db, Seed seed, DateOnly date, Guid productId, decimal quantity, decimal rate)
    {
        var created = await new CreateCreditNoteCommandHandler(db).Handle(
            new CreateCreditNoteCommand(
                seed.OrganizationId, seed.CustomerId, date, null,
                [new CreditNoteLineInput(productId, quantity, rate, VatRate.NoVat)]),
            CancellationToken.None);

        var approved = await new ApproveCreditNoteCommandHandler(
            db, seed.NumberGenerator, new FakeCurrentUserService(Guid.NewGuid()),
            new CreditNotePostingRule(), new StockLedgerService(db))
            .Handle(new ApproveCreditNoteCommand(seed.OrganizationId, created.Id), CancellationToken.None);

        return (approved.Id, approved.Code);
    }
}
