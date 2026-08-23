using ErpApp.Application.Accounting;
using ErpApp.Application.Accounting.Commands.ApproveJournalVoucher;
using ErpApp.Application.Accounting.Commands.CreateAccount;
using ErpApp.Application.Accounting.Commands.CreateAccountGroup;
using ErpApp.Application.Accounting.Commands.CreateJournalVoucher;
using ErpApp.Application.Accounting.Posting;
using ErpApp.Application.Accounting.Queries.RatioAnalysis;
using ErpApp.Application.Catalog.Commands.CreateProduct;
using ErpApp.Application.Catalog.Commands.CreateProductCategory;
using ErpApp.Application.Catalog.Commands.CreateUnitOfMeasurement;
using ErpApp.Application.Common.Persistence;
using ErpApp.Application.Common.Trees;
using ErpApp.Application.Contacts.Commands.CreateContact;
using ErpApp.Application.Inventory.Stock;
using ErpApp.Application.Sales;
using ErpApp.Application.Sales.Commands.ApproveInvoice;
using ErpApp.Application.Sales.Commands.CreateInvoice;
using ErpApp.Application.Sales.Posting;
using ErpApp.Application.Sales.Stock;
using ErpApp.Application.Tenancy.Commands.CreateWarehouse;
using ErpApp.Application.UnitTests.TestSupport;
using ErpApp.Domain.Accounting;
using ErpApp.Domain.Catalog;
using ErpApp.Domain.Contacts;
using ErpApp.Domain.Tenancy;

namespace ErpApp.Application.UnitTests.Accounting;

public class RatioAnalysisQueryHandlerTests
{
    /// <summary>Phase 19 decision #6 -- computed directly from BalanceSheetQueryHandler/
    /// IncomeStatementQueryHandler's own internals plus TenantSettings' AR/AP/Inventory defaults
    /// (decision #6's "Current Assets/Liabilities" approximation, since this codebase's Chart of
    /// Accounts has no Current/Non-current classification). Every figure here is hand-computable:
    /// a 10,000 cash capital injection, one Approved Service Invoice for 3,000 (no COGS, since a
    /// Service line never gets a CogsUnitCost), and one JournalVoucher-posted 2,000 Accounts
    /// Payable balance (Rent, unrelated to the Invoice) -- Assets 13,000 = Liabilities 2,000 +
    /// Equity 11,000 (10,000 capital + 1,000 net income).</summary>
    [Fact]
    public async Task Handle_computes_every_ratio_from_hand_computable_seeded_figures()
    {
        var db = TestAppDbContext.Create();
        var organizationId = Guid.NewGuid();
        var numberGenerator = new FakeDocumentNumberGenerator();
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        var customer = await new CreateContactCommandHandler(db, numberGenerator).Handle(
            new CreateContactCommand(organizationId, ContactType.Customer, "Acme Traders", null, null, null, null, null, 0m),
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
                VatRate.NoVat, 0, false),
            CancellationToken.None);

        var assetGroup = await new CreateAccountGroupCommandHandler(db).Handle(
            new CreateAccountGroupCommand(organizationId, "Current Assets", AccountRootType.Asset, null), CancellationToken.None);
        var liabilityGroup = await new CreateAccountGroupCommandHandler(db).Handle(
            new CreateAccountGroupCommand(organizationId, "Current Liabilities", AccountRootType.Liability, null), CancellationToken.None);
        var equityGroup = await new CreateAccountGroupCommandHandler(db).Handle(
            new CreateAccountGroupCommand(organizationId, "Owner's Equity", AccountRootType.Equity, null), CancellationToken.None);
        var incomeGroup = await new CreateAccountGroupCommandHandler(db).Handle(
            new CreateAccountGroupCommand(organizationId, "Sales Income", AccountRootType.Income, null), CancellationToken.None);
        var expenseGroup = await new CreateAccountGroupCommandHandler(db).Handle(
            new CreateAccountGroupCommand(organizationId, "Operating Expenses", AccountRootType.Expense, null), CancellationToken.None);

        var cash = await new CreateAccountCommandHandler(db, numberGenerator).Handle(
            new CreateAccountCommand(organizationId, "Cash in Hand", assetGroup.Id, AccountKind.Cash), CancellationToken.None);
        var capital = await new CreateAccountCommandHandler(db, numberGenerator).Handle(
            new CreateAccountCommand(organizationId, "Owner Capital", equityGroup.Id), CancellationToken.None);
        var ap = await new CreateAccountCommandHandler(db, numberGenerator).Handle(
            new CreateAccountCommand(organizationId, "Accounts Payable", liabilityGroup.Id), CancellationToken.None);
        var rent = await new CreateAccountCommandHandler(db, numberGenerator).Handle(
            new CreateAccountCommand(organizationId, "Rent Expense", expenseGroup.Id), CancellationToken.None);
        var ar = await new CreateAccountCommandHandler(db, numberGenerator).Handle(
            new CreateAccountCommand(organizationId, "Accounts Receivable", assetGroup.Id), CancellationToken.None);
        var sales = await new CreateAccountCommandHandler(db, numberGenerator).Handle(
            new CreateAccountCommand(organizationId, "Sales Revenue", incomeGroup.Id), CancellationToken.None);
        var vatPayable = await new CreateAccountCommandHandler(db, numberGenerator).Handle(
            new CreateAccountCommand(organizationId, "VAT Payable", liabilityGroup.Id), CancellationToken.None);
        var inventory = await new CreateAccountCommandHandler(db, numberGenerator).Handle(
            new CreateAccountCommand(organizationId, "Inventory", assetGroup.Id), CancellationToken.None);

        var settings = TenantSettings.CreateDefault(organizationId);
        settings.SetAccountingDefaults(sales.Id, ar.Id, vatPayable.Id, null, ap.Id, null, null);
        settings.SetInventoryDefaults(inventory.Id, null, null);
        db.TenantSettings.Add(settings);
        await db.SaveChangesAsync(CancellationToken.None);

        // 10,000 cash capital injection.
        var jv1 = await new CreateJournalVoucherCommandHandler(db).Handle(
            new CreateJournalVoucherCommand(
                organizationId, today, null,
                [new JournalVoucherLineInput(cash.Id, 10000m, 0), new JournalVoucherLineInput(capital.Id, 0, 10000m)]),
            CancellationToken.None);
        await new ApproveJournalVoucherCommandHandler(
            db, numberGenerator, new FakeCurrentUserService(Guid.NewGuid()), new JournalVoucherPostingRule())
            .Handle(new ApproveJournalVoucherCommand(organizationId, jv1.Id), CancellationToken.None);

        // 2,000 Rent on credit -> Accounts Payable.
        var jv2 = await new CreateJournalVoucherCommandHandler(db).Handle(
            new CreateJournalVoucherCommand(
                organizationId, today, null,
                [new JournalVoucherLineInput(rent.Id, 2000m, 0), new JournalVoucherLineInput(ap.Id, 0, 2000m)]),
            CancellationToken.None);
        await new ApproveJournalVoucherCommandHandler(
            db, numberGenerator, new FakeCurrentUserService(Guid.NewGuid()), new JournalVoucherPostingRule())
            .Handle(new ApproveJournalVoucherCommand(organizationId, jv2.Id), CancellationToken.None);

        // A 3,000 Service Invoice -- no COGS (a Service line never gets a CogsUnitCost).
        var invoice = await new CreateInvoiceCommandHandler(db).Handle(
            new CreateInvoiceCommand(
                organizationId, customer.Id, warehouse.Id, today, null,
                [new InvoiceLineInput(product.Id, 1m, 3000m, VatRate.NoVat)]),
            CancellationToken.None);
        var stockLedgerService = new StockLedgerService(db);
        await new ApproveInvoiceCommandHandler(
            db, numberGenerator, new FakeCurrentUserService(Guid.NewGuid()), new InvoicePostingRule(),
            new FifoStockAvailabilityPolicy(db, stockLedgerService), stockLedgerService)
            .Handle(new ApproveInvoiceCommand(organizationId, invoice.Id, OverrideWarning: false), CancellationToken.None);

        var handler = new RatioAnalysisQueryHandler(db, new AccountGroupTreeQuery(db));
        var result = await handler.Handle(new RatioAnalysisQuery(organizationId, today, today), CancellationToken.None);

        // Receivables 3,000, Inventory 0, Cash&Bank 10,000, Payables 2,000.
        Assert.Equal(6.5m, result.CurrentRatio); // (3000+0+10000)/2000
        Assert.Equal(6.5m, result.QuickRatio); // (3000+10000)/2000 -- Inventory is 0 so equal
        Assert.Equal(5m, result.CashRatio); // 10000/2000

        Assert.Equal(2000m / 11000m, result.DebtToEquityRatio); // Liabilities 2000 / Equity 11000
        Assert.Equal(2000m / 13000m, result.DebtRatio); // Liabilities 2000 / Assets 13000

        Assert.Equal(1m, result.ReceivablesTurnover); // Sales 3000 / Receivables 3000
        Assert.Equal(0m, result.InventoryTurnover); // CostOfSales 0 / Inventory 0 -- safe-divide zero

        Assert.Equal(100m, result.GrossProfitMarginPct); // (3000-0)/3000*100
        Assert.Equal(1000m / 3000m * 100m, result.NetProfitMarginPct); // NetProfit 1000 (3000 sales - 2000 rent) / Sales 3000
        Assert.Equal(1000m / 13000m * 100m, result.ReturnOnAssetsPct);
        Assert.Equal(1000m / 11000m * 100m, result.ReturnOnEquityPct);
    }
}
