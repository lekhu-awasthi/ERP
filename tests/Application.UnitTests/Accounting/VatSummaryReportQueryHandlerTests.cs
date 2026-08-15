using ErpApp.Application.Accounting;
using ErpApp.Application.Accounting.Commands.CreateAccount;
using ErpApp.Application.Accounting.Commands.CreateAccountGroup;
using ErpApp.Application.Accounting.Posting;
using ErpApp.Application.Accounting.Queries.VatSummaryReport;
using ErpApp.Application.Catalog.Commands.CreateProduct;
using ErpApp.Application.Catalog.Commands.CreateProductCategory;
using ErpApp.Application.Catalog.Commands.CreateUnitOfMeasurement;
using ErpApp.Application.Common.Persistence;
using ErpApp.Application.Contacts.Commands.CreateContact;
using ErpApp.Application.Inventory.Stock;
using ErpApp.Application.Purchasing;
using ErpApp.Application.Purchasing.Commands.ApproveDebitNote;
using ErpApp.Application.Purchasing.Commands.ApprovePurchaseBill;
using ErpApp.Application.Purchasing.Commands.CreateDebitNote;
using ErpApp.Application.Purchasing.Commands.CreatePurchaseBill;
using ErpApp.Application.Purchasing.Posting;
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
using ErpApp.Domain.Contacts;
using ErpApp.Domain.Purchasing;
using ErpApp.Domain.Tenancy;

namespace ErpApp.Application.UnitTests.Accounting;

public class VatSummaryReportQueryHandlerTests
{
    [Fact]
    public async Task Handle_nets_credit_notes_and_debit_notes_into_their_source_vat_rate_bucket_and_excludes_draft_and_out_of_range_documents()
    {
        var db = TestAppDbContext.Create();
        var seed = await SeedAsync(db);

        // Sales side: two Invoice lines in two different VatRate buckets, netted against a
        // same-bucket standalone CreditNote. A Draft invoice and an out-of-range invoice must both
        // be excluded entirely.
        await CreateAndApproveInvoiceAsync(db, seed, new DateOnly(2026, 1, 15), 10m, 100m, VatRate.ThirteenPercentVat);
        await CreateAndApproveInvoiceAsync(db, seed, new DateOnly(2026, 1, 16), 5m, 50m, VatRate.NoVat);
        await CreateInvoiceAsync(db, seed, new DateOnly(2026, 1, 20), 5m, 50m, VatRate.ThirteenPercentVat);
        await CreateAndApproveInvoiceAsync(db, seed, new DateOnly(2026, 3, 1), 2m, 20m, VatRate.ThirteenPercentVat);
        await CreateAndApproveStandaloneCreditNoteAsync(db, seed, new DateOnly(2026, 1, 18), 2m, 100m, VatRate.ThirteenPercentVat);

        // Purchase side: one PurchaseBill line netted against a same-bucket standalone DebitNote.
        await CreateAndApprovePurchaseBillAsync(db, seed, new DateOnly(2026, 1, 10), 4m, 100m, VatRate.ThirteenPercentVat);
        await CreateAndApproveStandaloneDebitNoteAsync(db, seed, new DateOnly(2026, 1, 12), 1m, 100m, VatRate.ThirteenPercentVat);

        var handler = new VatSummaryReportQueryHandler(db);
        var result = await handler.Handle(
            new VatSummaryReportQuery(seed.OrganizationId, new DateOnly(2026, 1, 1), new DateOnly(2026, 1, 31)),
            CancellationToken.None);

        Assert.Equal(3, result.SalesBuckets.Count);
        Assert.Equal(3, result.PurchaseBuckets.Count);

        var thirteenPercentSales = Assert.Single(result.SalesBuckets, b => b.VatRate == VatRate.ThirteenPercentVat);
        // Invoice 10x100=1000 (VAT 130) minus CreditNote 2x100=200 (VAT 26) -> 800 / 104.
        Assert.Equal(800m, thirteenPercentSales.NetSalesAmount);
        Assert.Equal(104m, thirteenPercentSales.OutputVatAmount);

        var noVatSales = Assert.Single(result.SalesBuckets, b => b.VatRate == VatRate.NoVat);
        Assert.Equal(250m, noVatSales.NetSalesAmount);
        Assert.Equal(0m, noVatSales.OutputVatAmount);

        var zeroVatSales = Assert.Single(result.SalesBuckets, b => b.VatRate == VatRate.ZeroVat);
        Assert.Equal(0m, zeroVatSales.NetSalesAmount);
        Assert.Equal(0m, zeroVatSales.OutputVatAmount);

        var thirteenPercentPurchase = Assert.Single(result.PurchaseBuckets, b => b.VatRate == VatRate.ThirteenPercentVat);
        // PurchaseBill 4x100=400 (VAT 52) minus DebitNote 1x100=100 (VAT 13) -> 300 / 39.
        Assert.Equal(300m, thirteenPercentPurchase.NetPurchaseAmount);
        Assert.Equal(39m, thirteenPercentPurchase.InputVatAmount);

        Assert.Equal(104m, result.TotalOutputVat);
        Assert.Equal(39m, result.TotalInputVat);
        Assert.Equal(65m, result.NetVatPayable);
    }

    [Fact]
    public async Task Handle_surfaces_a_negative_net_vat_payable_when_input_vat_exceeds_output_vat()
    {
        var db = TestAppDbContext.Create();
        var seed = await SeedAsync(db);

        await CreateAndApproveInvoiceAsync(db, seed, new DateOnly(2026, 1, 5), 1m, 100m, VatRate.ThirteenPercentVat);
        await CreateAndApprovePurchaseBillAsync(db, seed, new DateOnly(2026, 1, 6), 10m, 100m, VatRate.ThirteenPercentVat);

        var handler = new VatSummaryReportQueryHandler(db);
        var result = await handler.Handle(
            new VatSummaryReportQuery(seed.OrganizationId, new DateOnly(2026, 1, 1), new DateOnly(2026, 1, 31)),
            CancellationToken.None);

        // Output VAT 13 (1x100x13%), Input VAT 130 (10x100x13%) -> refundable credit, not clamped to zero.
        Assert.Equal(13m, result.TotalOutputVat);
        Assert.Equal(130m, result.TotalInputVat);
        Assert.Equal(-117m, result.NetVatPayable);
    }

    // See SalesMasterReportQueryHandlerTests' matching comment: a single FakeDocumentNumberGenerator
    // instance is shared across every Create/Approve call in one test, or every approved document
    // collides on the same "Foo-0001" code.
    private sealed record Seed(
        Guid OrganizationId, FakeDocumentNumberGenerator NumberGenerator, Guid CustomerId, Guid SupplierId,
        Guid WarehouseId, Guid ProductId);

    private static async Task<Seed> SeedAsync(IAppDbContext db)
    {
        var organizationId = Guid.NewGuid();
        var numberGenerator = new FakeDocumentNumberGenerator();

        var customer = await new CreateContactCommandHandler(db, numberGenerator).Handle(
            new CreateContactCommand(organizationId, ContactType.Customer, "Acme Traders", null, null, null, null, null, 0m),
            CancellationToken.None);
        var supplier = await new CreateContactCommandHandler(db, numberGenerator).Handle(
            new CreateContactCommand(organizationId, ContactType.Supplier, "Global Supplies", null, null, null, null, null, 0m),
            CancellationToken.None);

        var warehouse = await new CreateWarehouseCommandHandler(db).Handle(
            new CreateWarehouseCommand(organizationId, "Main Warehouse"), CancellationToken.None);

        var category = await new CreateProductCategoryCommandHandler(db).Handle(
            new CreateProductCategoryCommand(organizationId, "Services", null), CancellationToken.None);
        var unit = await new CreateUnitOfMeasurementCommandHandler(db).Handle(
            new CreateUnitOfMeasurementCommand(organizationId, "Hour", "hr"), CancellationToken.None);

        var product = await new CreateProductCommandHandler(db, numberGenerator).Handle(
            new CreateProductCommand(
                organizationId, ProductType.Service, "Consulting", category.Id, unit.Id, null, true, 100m, 80m,
                VatRate.ThirteenPercentVat, 0, false),
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

        var settings = TenantSettings.CreateDefault(organizationId);
        settings.SetAccountingDefaults(sales.Id, ar.Id, vatPayable.Id, purchase.Id, ap.Id, vatReceivable.Id, null);
        db.TenantSettings.Add(settings);
        await db.SaveChangesAsync(CancellationToken.None);

        return new Seed(organizationId, numberGenerator, customer.Id, supplier.Id, warehouse.Id, product.Id);
    }

    private static async Task<CreateInvoiceResult> CreateInvoiceAsync(
        IAppDbContext db, Seed seed, DateOnly date, decimal quantity, decimal rate, VatRate vatRate)
    {
        return await new CreateInvoiceCommandHandler(db).Handle(
            new CreateInvoiceCommand(
                seed.OrganizationId, seed.CustomerId, seed.WarehouseId, date, null,
                [new InvoiceLineInput(seed.ProductId, quantity, rate, vatRate)]),
            CancellationToken.None);
    }

    private static async Task CreateAndApproveInvoiceAsync(
        IAppDbContext db, Seed seed, DateOnly date, decimal quantity, decimal rate, VatRate vatRate)
    {
        var created = await CreateInvoiceAsync(db, seed, date, quantity, rate, vatRate);
        var stockLedgerService = new StockLedgerService(db);
        await new ApproveInvoiceCommandHandler(
            db, seed.NumberGenerator, new FakeCurrentUserService(Guid.NewGuid()), new InvoicePostingRule(),
            new FifoStockAvailabilityPolicy(db, stockLedgerService), stockLedgerService)
            .Handle(new ApproveInvoiceCommand(seed.OrganizationId, created.Id, OverrideWarning: false), CancellationToken.None);
    }

    private static async Task CreateAndApproveStandaloneCreditNoteAsync(
        IAppDbContext db, Seed seed, DateOnly date, decimal quantity, decimal rate, VatRate vatRate)
    {
        var created = await new CreateCreditNoteCommandHandler(db).Handle(
            new CreateCreditNoteCommand(
                seed.OrganizationId, seed.CustomerId, date, null,
                [new CreditNoteLineInput(seed.ProductId, quantity, rate, vatRate)]),
            CancellationToken.None);

        await new ApproveCreditNoteCommandHandler(
            db, seed.NumberGenerator, new FakeCurrentUserService(Guid.NewGuid()),
            new CreditNotePostingRule(), new StockLedgerService(db))
            .Handle(new ApproveCreditNoteCommand(seed.OrganizationId, created.Id), CancellationToken.None);
    }

    private static async Task CreateAndApprovePurchaseBillAsync(
        IAppDbContext db, Seed seed, DateOnly date, decimal quantity, decimal rate, VatRate vatRate)
    {
        var created = await new CreatePurchaseBillCommandHandler(db).Handle(
            new CreatePurchaseBillCommand(
                seed.OrganizationId, seed.SupplierId, seed.WarehouseId, date, null, null, false, null, null, null, null,
                [new PurchaseBillLineInput(seed.ProductId, quantity, rate, vatRate, ExpenditureClassification.Others)]),
            CancellationToken.None);

        await new ApprovePurchaseBillCommandHandler(
            db, seed.NumberGenerator, new FakeCurrentUserService(Guid.NewGuid()), new PurchaseBillPostingRule(),
            new StockLedgerService(db))
            .Handle(new ApprovePurchaseBillCommand(seed.OrganizationId, created.Id), CancellationToken.None);
    }

    private static async Task CreateAndApproveStandaloneDebitNoteAsync(
        IAppDbContext db, Seed seed, DateOnly date, decimal quantity, decimal rate, VatRate vatRate)
    {
        var created = await new CreateDebitNoteCommandHandler(db).Handle(
            new CreateDebitNoteCommand(
                seed.OrganizationId, seed.SupplierId, date, null, null,
                [new DebitNoteLineInput(seed.ProductId, quantity, rate, vatRate)]),
            CancellationToken.None);

        await new ApproveDebitNoteCommandHandler(
            db, seed.NumberGenerator, new FakeCurrentUserService(Guid.NewGuid()),
            new DebitNotePostingRule(), new StockLedgerService(db))
            .Handle(new ApproveDebitNoteCommand(seed.OrganizationId, created.Id), CancellationToken.None);
    }
}
