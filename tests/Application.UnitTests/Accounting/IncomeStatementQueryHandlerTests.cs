using ErpApp.Application.Accounting;
using ErpApp.Application.Accounting.Commands.ApproveJournalVoucher;
using ErpApp.Application.Accounting.Commands.CreateAccount;
using ErpApp.Application.Accounting.Commands.CreateAccountGroup;
using ErpApp.Application.Accounting.Commands.CreateJournalVoucher;
using ErpApp.Application.Accounting.Posting;
using ErpApp.Application.Accounting.Queries.IncomeStatement;
using ErpApp.Application.Catalog.Commands.CreateProduct;
using ErpApp.Application.Catalog.Commands.CreateProductCategory;
using ErpApp.Application.Catalog.Commands.CreateUnitOfMeasurement;
using ErpApp.Application.Common.Persistence;
using ErpApp.Application.Contacts.Commands.CreateContact;
using ErpApp.Application.Inventory.Stock;
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
using ErpApp.Domain.Contacts;
using ErpApp.Domain.Purchasing;
using ErpApp.Domain.Tenancy;

namespace ErpApp.Application.UnitTests.Accounting;

public class IncomeStatementQueryHandlerTests
{
    /// <summary>Regression coverage for the post-Phase-19 Purchase/COGS double-count fix (see
    /// PurchaseBillAccountResolver's doc comment and docs/phase-7-status.md's addendum): a Goods
    /// PurchaseBill debits Inventory (Asset), not the tenant's Purchase Expense account, so buying
    /// 10 units for 400 and then selling 5 of them (FIFO cost 200) must recognise expense exactly
    /// once -- 200 of COGS -- not 400 of Purchase Expense plus 200 of COGS. Before the fix this
    /// scenario's Net Profit came back 300 short (500 sales - 400 purchase - 200 cogs = -100,
    /// instead of the correct 500 - 200 = 300).</summary>
    [Fact]
    public async Task Handle_recognises_goods_cost_as_cogs_only_once_not_also_as_purchase_expense()
    {
        var db = TestAppDbContext.Create();
        var organizationId = Guid.NewGuid();
        var numberGenerator = new FakeDocumentNumberGenerator();
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        var supplier = await new CreateContactCommandHandler(db, numberGenerator).Handle(
            new CreateContactCommand(organizationId, ContactType.Supplier, "Global Supplies", null, null, null, null, null, 0m),
            CancellationToken.None);
        var customer = await new CreateContactCommandHandler(db, numberGenerator).Handle(
            new CreateContactCommand(organizationId, ContactType.Customer, "Acme Traders", null, null, null, null, null, 0m),
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
        var incomeGroup = await new CreateAccountGroupCommandHandler(db).Handle(
            new CreateAccountGroupCommand(organizationId, "Sales Income", AccountRootType.Income, null), CancellationToken.None);
        var expenseGroup = await new CreateAccountGroupCommandHandler(db).Handle(
            new CreateAccountGroupCommand(organizationId, "Operating Expenses", AccountRootType.Expense, null), CancellationToken.None);

        var ap = await new CreateAccountCommandHandler(db, numberGenerator).Handle(
            new CreateAccountCommand(organizationId, "Accounts Payable", liabilityGroup.Id), CancellationToken.None);
        var ar = await new CreateAccountCommandHandler(db, numberGenerator).Handle(
            new CreateAccountCommand(organizationId, "Accounts Receivable", assetGroup.Id), CancellationToken.None);
        var sales = await new CreateAccountCommandHandler(db, numberGenerator).Handle(
            new CreateAccountCommand(organizationId, "Sales Revenue", incomeGroup.Id), CancellationToken.None);
        var vatPayable = await new CreateAccountCommandHandler(db, numberGenerator).Handle(
            new CreateAccountCommand(organizationId, "VAT Payable", liabilityGroup.Id), CancellationToken.None);
        var purchase = await new CreateAccountCommandHandler(db, numberGenerator).Handle(
            new CreateAccountCommand(organizationId, "Purchase Expense", expenseGroup.Id), CancellationToken.None);
        var inventory = await new CreateAccountCommandHandler(db, numberGenerator).Handle(
            new CreateAccountCommand(organizationId, "Inventory", assetGroup.Id), CancellationToken.None);
        var cogs = await new CreateAccountCommandHandler(db, numberGenerator).Handle(
            new CreateAccountCommand(organizationId, "Cost of Goods Sold", expenseGroup.Id), CancellationToken.None);

        var settings = TenantSettings.CreateDefault(organizationId);
        settings.SetAccountingDefaults(sales.Id, ar.Id, vatPayable.Id, purchase.Id, ap.Id, null, null);
        settings.SetInventoryDefaults(inventory.Id, cogs.Id, null, null);
        db.TenantSettings.Add(settings);
        await db.SaveChangesAsync(CancellationToken.None);

        // Buy 10 units @ 40 = 400 -- must debit Inventory, not Purchase Expense.
        var bill = await new CreatePurchaseBillCommandHandler(db).Handle(
            new CreatePurchaseBillCommand(
                organizationId, supplier.Id, warehouse.Id, today, null, null, false,
                null, null, null, null,
                [new PurchaseBillLineInput(product.Id, 10m, 40m, VatRate.NoVat, ExpenditureClassification.Others)]),
            CancellationToken.None);
        var stockLedgerService = new StockLedgerService(db);
        await new ApprovePurchaseBillCommandHandler(
            db, numberGenerator, new FakeCurrentUserService(Guid.NewGuid()), new PurchaseBillPostingRule(), stockLedgerService)
            .Handle(new ApprovePurchaseBillCommand(organizationId, bill.Id), CancellationToken.None);

        // Sell 5 of those units for 500 -- FIFO COGS relief of 5 * 40 = 200.
        var invoice = await new CreateInvoiceCommandHandler(db).Handle(
            new CreateInvoiceCommand(
                organizationId, customer.Id, warehouse.Id, today, null,
                [new InvoiceLineInput(product.Id, 5m, 100m, VatRate.NoVat)]),
            CancellationToken.None);
        await new ApproveInvoiceCommandHandler(
            db, numberGenerator, new FakeCurrentUserService(Guid.NewGuid()), new InvoicePostingRule(),
            new FifoStockAvailabilityPolicy(db, stockLedgerService), stockLedgerService)
            .Handle(new ApproveInvoiceCommand(organizationId, invoice.Id, OverrideWarning: false), CancellationToken.None);

        var handler = new IncomeStatementQueryHandler(db);
        var result = await handler.Handle(
            new IncomeStatementQuery(organizationId, today.AddDays(-1), today.AddDays(1)), CancellationToken.None);

        Assert.Equal(500m, result.TotalIncome);

        var expenseRow = Assert.Single(result.ExpenseRows);
        Assert.Equal(cogs.Id, expenseRow.AccountId);
        Assert.Equal(200m, expenseRow.Amount);
        Assert.DoesNotContain(result.ExpenseRows, r => r.AccountId == purchase.Id);

        Assert.Equal(200m, result.TotalExpense);
        Assert.Equal(300m, result.NetIncome);
    }

    [Fact]
    public async Task Handle_nets_income_and_expense_and_computes_net_income_within_the_date_range()
    {
        var db = TestAppDbContext.Create();
        var (organizationId, cashAccountId, salesAccountId, apAccountId, rentExpenseAccountId) = await SeedAccountsAsync(db);

        await ApproveJournalVoucherAsync(db, organizationId, cashAccountId, salesAccountId, 3000m);
        await ApproveJournalVoucherAsync(db, organizationId, rentExpenseAccountId, apAccountId, 2000m);

        var handler = new IncomeStatementQueryHandler(db);
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var result = await handler.Handle(
            new IncomeStatementQuery(organizationId, today.AddDays(-1), today.AddDays(1)), CancellationToken.None);

        var incomeRow = Assert.Single(result.IncomeRows);
        Assert.Equal(salesAccountId, incomeRow.AccountId);
        Assert.Equal(3000m, incomeRow.Amount);

        var expenseRow = Assert.Single(result.ExpenseRows);
        Assert.Equal(rentExpenseAccountId, expenseRow.AccountId);
        Assert.Equal(2000m, expenseRow.Amount);

        Assert.Equal(3000m, result.TotalIncome);
        Assert.Equal(2000m, result.TotalExpense);
        Assert.Equal(1000m, result.NetIncome);
    }

    [Fact]
    public async Task Handle_excludes_entries_posted_outside_the_date_range()
    {
        var db = TestAppDbContext.Create();
        var (organizationId, cashAccountId, salesAccountId, _, _) = await SeedAccountsAsync(db);
        await ApproveJournalVoucherAsync(db, organizationId, cashAccountId, salesAccountId, 3000m);

        var handler = new IncomeStatementQueryHandler(db);
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var result = await handler.Handle(
            new IncomeStatementQuery(organizationId, today.AddDays(-10), today.AddDays(-2)), CancellationToken.None);

        Assert.Empty(result.IncomeRows);
        Assert.Empty(result.ExpenseRows);
        Assert.Equal(0m, result.TotalIncome);
        Assert.Equal(0m, result.TotalExpense);
        Assert.Equal(0m, result.NetIncome);
    }

    private static async Task<(Guid OrganizationId, Guid CashAccountId, Guid SalesAccountId, Guid ApAccountId, Guid RentExpenseAccountId)>
        SeedAccountsAsync(IAppDbContext db)
    {
        var organizationId = Guid.NewGuid();
        var numberGenerator = new FakeDocumentNumberGenerator();

        var assetGroup = await new CreateAccountGroupCommandHandler(db).Handle(
            new CreateAccountGroupCommand(organizationId, "Current Assets", AccountRootType.Asset, null), CancellationToken.None);
        var liabilityGroup = await new CreateAccountGroupCommandHandler(db).Handle(
            new CreateAccountGroupCommand(organizationId, "Current Liabilities", AccountRootType.Liability, null), CancellationToken.None);
        var incomeGroup = await new CreateAccountGroupCommandHandler(db).Handle(
            new CreateAccountGroupCommand(organizationId, "Sales Income", AccountRootType.Income, null), CancellationToken.None);
        var expenseGroup = await new CreateAccountGroupCommandHandler(db).Handle(
            new CreateAccountGroupCommand(organizationId, "Operating Expenses", AccountRootType.Expense, null), CancellationToken.None);

        var cash = await new CreateAccountCommandHandler(db, numberGenerator).Handle(
            new CreateAccountCommand(organizationId, "Cash in Hand", assetGroup.Id), CancellationToken.None);
        var sales = await new CreateAccountCommandHandler(db, numberGenerator).Handle(
            new CreateAccountCommand(organizationId, "Sales Revenue", incomeGroup.Id), CancellationToken.None);
        var ap = await new CreateAccountCommandHandler(db, numberGenerator).Handle(
            new CreateAccountCommand(organizationId, "Accounts Payable", liabilityGroup.Id), CancellationToken.None);
        var rent = await new CreateAccountCommandHandler(db, numberGenerator).Handle(
            new CreateAccountCommand(organizationId, "Rent Expense", expenseGroup.Id), CancellationToken.None);

        return (organizationId, cash.Id, sales.Id, ap.Id, rent.Id);
    }

    private static async Task ApproveJournalVoucherAsync(
        IAppDbContext db, Guid organizationId, Guid debitAccountId, Guid creditAccountId, decimal amount)
    {
        var created = await new CreateJournalVoucherCommandHandler(db).Handle(
            new CreateJournalVoucherCommand(
                organizationId, new DateOnly(2026, 1, 1), null,
                [new JournalVoucherLineInput(debitAccountId, amount, 0m), new JournalVoucherLineInput(creditAccountId, 0m, amount)]),
            CancellationToken.None);

        await new ApproveJournalVoucherCommandHandler(
            db, new FakeDocumentNumberGenerator(), new FakeCurrentUserService(Guid.NewGuid()), new JournalVoucherPostingRule())
            .Handle(new ApproveJournalVoucherCommand(organizationId, created.Id), CancellationToken.None);
    }
}
