using ErpApp.Application.Accounting;
using ErpApp.Application.Accounting.Commands.CreateAccount;
using ErpApp.Application.Accounting.Commands.CreateAccountGroup;
using ErpApp.Application.Catalog.Commands.CreateProduct;
using ErpApp.Application.Catalog.Commands.CreateProductCategory;
using ErpApp.Application.Catalog.Commands.CreateUnitOfMeasurement;
using ErpApp.Application.Common.Exceptions;
using ErpApp.Application.Common.Persistence;
using ErpApp.Application.Configuration.Commands.CreateTdsType;
using ErpApp.Application.Contacts.Commands.CreateContact;
using ErpApp.Application.Contacts.Queries.ContactOverview;
using ErpApp.Application.Inventory.Stock;
using ErpApp.Application.Payments;
using ErpApp.Application.Payments.Commands.ApprovePayment;
using ErpApp.Application.Payments.Commands.CreatePayment;
using ErpApp.Application.Payments.Posting;
using ErpApp.Application.Purchasing;
using ErpApp.Application.Purchasing.Commands.ApprovePurchaseBill;
using ErpApp.Application.Purchasing.Commands.CreatePurchaseBill;
using ErpApp.Application.Purchasing.Posting;
using ErpApp.Application.Sales;
using ErpApp.Application.Sales.Commands.ApproveInvoice;
using ErpApp.Application.Sales.Commands.CreateInvoice;
using ErpApp.Application.Sales.Posting;
using ErpApp.Application.Sales.Stock;
using ErpApp.Application.Tenancy.Commands.CreateWarehouse;
using ErpApp.Application.UnitTests.TestSupport;
using ErpApp.Domain.Accounting;
using ErpApp.Domain.Catalog;
using ErpApp.Domain.Common;
using ErpApp.Domain.Contacts;
using ErpApp.Domain.Payments;
using ErpApp.Domain.Purchasing;
using ErpApp.Domain.Tenancy;

namespace ErpApp.Application.UnitTests.Contacts;

public class ContactOverviewQueryHandlerTests
{
    [Fact]
    public async Task Handle_computes_closing_balance_matching_what_a_statement_query_would_return_for_the_same_customer()
    {
        var db = TestAppDbContext.Create();
        var seed = await SeedAsync(db);
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        // Everything dated on/before "today" (the handler's own AsOfDate, hardcoded server-side)
        // participates -- Contact.OpeningBalance (1000) + this Invoice (500) = 1500 closing.
        await CreateAndApproveInvoiceAsync(db, seed, today.AddDays(-5), 500m, seed.CustomerId);

        var query = new ContactOverviewQuery(seed.OrganizationId, seed.CustomerId);
        Assert.Equal("Contacts.Contact.View", query.PermissionKey);

        var handler = new ContactOverviewQueryHandler(db);
        var result = await handler.Handle(query, CancellationToken.None);

        Assert.Equal(1000m, result.OpeningBalance);
        Assert.Equal("DR", result.OpeningBalanceType);
        Assert.Equal(1500m, result.ClosingBalance);
        Assert.Equal("DR", result.ClosingBalanceType);

        Assert.Single(result.RecentTransactions);
        Assert.Equal(DocumentType.Invoice, result.RecentTransactions[0].DocumentType);
        Assert.Equal(500m, result.RecentTransactions[0].Debit);
        Assert.Equal(0m, result.RecentTransactions[0].Credit);
    }

    [Fact]
    public async Task Handle_computes_supplier_polarity_net_of_tds()
    {
        var db = TestAppDbContext.Create();
        var seed = await SeedAsync(db);
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        var bill = await CreateAndApprovePurchaseBillAsync(db, seed, today.AddDays(-10), 1000m, seed.TdsTypeId);
        await CreateAndApprovePaymentAsync(db, seed, today.AddDays(-2), 400m, bill.Id);

        var query = new ContactOverviewQuery(seed.OrganizationId, seed.SupplierId);
        var handler = new ContactOverviewQueryHandler(db);
        var result = await handler.Handle(query, CancellationToken.None);

        Assert.Equal(0m, result.OpeningBalance);
        Assert.Equal("CR", result.OpeningBalanceType);
        // 1000 gross - 100 TDS = 900 payable, less 400 paid = 500 CR outstanding.
        Assert.Equal(500m, result.ClosingBalance);
        Assert.Equal("CR", result.ClosingBalanceType);

        Assert.Equal(2, result.RecentTransactions.Count);
        // Most-recent first.
        Assert.Equal(DocumentType.Payment, result.RecentTransactions[0].DocumentType);
        Assert.Equal(400m, result.RecentTransactions[0].Debit);
        Assert.Equal(DocumentType.PurchaseBill, result.RecentTransactions[1].DocumentType);
        Assert.Equal(900m, result.RecentTransactions[1].Credit);
    }

    [Fact]
    public async Task Handle_caps_recent_transactions_at_ten_most_recent_by_date()
    {
        var db = TestAppDbContext.Create();
        var seed = await SeedAsync(db);
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        for (var i = 0; i < 12; i++)
        {
            await CreateAndApproveInvoiceAsync(db, seed, today.AddDays(-(i + 1)), 10m * (i + 1), seed.CustomerId);
        }

        var handler = new ContactOverviewQueryHandler(db);
        var result = await handler.Handle(new ContactOverviewQuery(seed.OrganizationId, seed.CustomerId), CancellationToken.None);

        Assert.Equal(10, result.RecentTransactions.Count);
        // Most recent (yesterday, rate 10) comes first, not the oldest.
        Assert.Equal(10m, result.RecentTransactions[0].Debit);
        Assert.Equal(20m, result.RecentTransactions[1].Debit);

        // All 12 still fold into Closing Balance even though only 10 are surfaced as "recent".
        var expectedClosing = 1000m + Enumerable.Range(1, 12).Sum(i => 10m * i);
        Assert.Equal(expectedClosing, result.ClosingBalance);
    }

    [Fact]
    public async Task Handle_returns_opening_balance_only_for_a_contact_with_no_activity()
    {
        var db = TestAppDbContext.Create();
        var seed = await SeedAsync(db);

        var handler = new ContactOverviewQueryHandler(db);
        var result = await handler.Handle(new ContactOverviewQuery(seed.OrganizationId, seed.SupplierId), CancellationToken.None);

        Assert.Equal(0m, result.OpeningBalance);
        Assert.Equal(0m, result.ClosingBalance);
        Assert.Empty(result.RecentTransactions);
    }

    [Fact]
    public async Task Handle_returns_an_empty_ledger_for_a_lead_with_no_financial_activity()
    {
        var db = TestAppDbContext.Create();
        var seed = await SeedAsync(db);
        var numberGenerator = new FakeDocumentNumberGenerator();

        var lead = await new CreateContactCommandHandler(db, numberGenerator).Handle(
            new CreateContactCommand(seed.OrganizationId, ContactType.Lead, "Prospective Co", null, null, null, null, null, 250m),
            CancellationToken.None);

        var handler = new ContactOverviewQueryHandler(db);
        var result = await handler.Handle(new ContactOverviewQuery(seed.OrganizationId, lead.Id), CancellationToken.None);

        Assert.Equal(ContactType.Lead, result.ContactType);
        Assert.Equal(250m, result.OpeningBalance);
        Assert.Equal("DR", result.OpeningBalanceType);
        Assert.Equal(250m, result.ClosingBalance);
        Assert.Equal("DR", result.ClosingBalanceType);
        Assert.Empty(result.RecentTransactions);
    }

    [Fact]
    public async Task Handle_throws_not_found_for_an_unknown_contact()
    {
        var db = TestAppDbContext.Create();
        var seed = await SeedAsync(db);

        var handler = new ContactOverviewQueryHandler(db);

        await Assert.ThrowsAsync<NotFoundException>(() => handler.Handle(
            new ContactOverviewQuery(seed.OrganizationId, Guid.NewGuid()), CancellationToken.None));
    }

    private sealed record Seed(
        Guid OrganizationId, FakeDocumentNumberGenerator NumberGenerator, Guid CustomerId, Guid SupplierId,
        Guid WarehouseId, Guid ProductId, Guid CashAccountId, Guid TdsTypeId);

    private static async Task<Seed> SeedAsync(IAppDbContext db)
    {
        var organizationId = Guid.NewGuid();
        var numberGenerator = new FakeDocumentNumberGenerator();

        var customer = await new CreateContactCommandHandler(db, numberGenerator).Handle(
            new CreateContactCommand(organizationId, ContactType.Customer, "Acme Traders", null, null, null, null, null, 1000m),
            CancellationToken.None);
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
                organizationId, ProductType.Service, "Consulting", category.Id, unit.Id, null, true, 150m, 100m,
                VatRate.NoVat, 0, false),
            CancellationToken.None);

        var tdsType = await new CreateTdsTypeCommandHandler(db).Handle(
            new CreateTdsTypeCommand(organizationId, "TDS10", "10% TDS", 10m), CancellationToken.None);

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
        var tdsPayable = await new CreateAccountCommandHandler(db, numberGenerator).Handle(
            new CreateAccountCommand(organizationId, "TDS Payable", liabilityGroup.Id), CancellationToken.None);
        var cash = await new CreateAccountCommandHandler(db, numberGenerator).Handle(
            new CreateAccountCommand(organizationId, "Cash in Hand", assetGroup.Id), CancellationToken.None);
        var inventory = await new CreateAccountCommandHandler(db, numberGenerator).Handle(
            new CreateAccountCommand(organizationId, "Inventory", assetGroup.Id), CancellationToken.None);
        var cogs = await new CreateAccountCommandHandler(db, numberGenerator).Handle(
            new CreateAccountCommand(organizationId, "Cost of Goods Sold", expenseGroup.Id), CancellationToken.None);

        var settings = TenantSettings.CreateDefault(organizationId);
        settings.SetAccountingDefaults(sales.Id, ar.Id, vatPayable.Id, purchase.Id, ap.Id, vatReceivable.Id, tdsPayable.Id);
        settings.SetInventoryDefaults(inventory.Id, cogs.Id, null);
        db.TenantSettings.Add(settings);
        await db.SaveChangesAsync(CancellationToken.None);

        return new Seed(organizationId, numberGenerator, customer.Id, supplier.Id, warehouse.Id, product.Id, cash.Id, tdsType.Id);
    }

    private static async Task<(Guid Id, string Code)> CreateAndApproveInvoiceAsync(
        IAppDbContext db, Seed seed, DateOnly date, decimal rate, Guid contactId)
    {
        var created = await new CreateInvoiceCommandHandler(db).Handle(
            new CreateInvoiceCommand(
                seed.OrganizationId, contactId, seed.WarehouseId, date, null,
                [new InvoiceLineInput(seed.ProductId, 1m, rate, VatRate.NoVat)]),
            CancellationToken.None);

        var stockLedgerService = new StockLedgerService(db);
        var approved = await new ApproveInvoiceCommandHandler(
            db, seed.NumberGenerator, new FakeCurrentUserService(Guid.NewGuid()), new InvoicePostingRule(),
            new FifoStockAvailabilityPolicy(db, stockLedgerService), stockLedgerService)
            .Handle(new ApproveInvoiceCommand(seed.OrganizationId, created.Id, OverrideWarning: false), CancellationToken.None);
        return (approved.Id, approved.Code);
    }

    private static async Task<(Guid Id, string Code)> CreateAndApprovePurchaseBillAsync(
        IAppDbContext db, Seed seed, DateOnly date, decimal rate, Guid? tdsTypeId)
    {
        var created = await new CreatePurchaseBillCommandHandler(db).Handle(
            new CreatePurchaseBillCommand(
                seed.OrganizationId, seed.SupplierId, seed.WarehouseId, date, null, null, false, null, null, null, tdsTypeId,
                [new PurchaseBillLineInput(seed.ProductId, 1m, rate, VatRate.NoVat, ExpenditureClassification.Others)]),
            CancellationToken.None);

        var approved = await new ApprovePurchaseBillCommandHandler(
            db, seed.NumberGenerator, new FakeCurrentUserService(Guid.NewGuid()), new PurchaseBillPostingRule(),
            new StockLedgerService(db))
            .Handle(new ApprovePurchaseBillCommand(seed.OrganizationId, created.Id), CancellationToken.None);
        return (approved.Id, approved.Code);
    }

    private static async Task<(Guid Id, string Code)> CreateAndApprovePaymentAsync(
        IAppDbContext db, Seed seed, DateOnly date, decimal amount, Guid targetPurchaseBillId)
    {
        var created = await new CreatePaymentCommandHandler(db).Handle(
            new CreatePaymentCommand(
                seed.OrganizationId, seed.SupplierId, PaymentDirection.Paid, date, null, seed.CashAccountId, amount, null,
                [new PaymentAllocationInput(DocumentType.PurchaseBill, targetPurchaseBillId, amount)]),
            CancellationToken.None);

        var approved = await new ApprovePaymentCommandHandler(
            db, seed.NumberGenerator, new FakeCurrentUserService(Guid.NewGuid()), new PaymentPostingRule())
            .Handle(new ApprovePaymentCommand(seed.OrganizationId, created.Id), CancellationToken.None);

        return (approved.Id, approved.Code);
    }
}
