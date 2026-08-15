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
using ErpApp.Application.Contacts.Queries.ContactStatement;
using ErpApp.Application.Inventory.Stock;
using ErpApp.Application.Payments;
using ErpApp.Application.Payments.Commands.ApprovePayment;
using ErpApp.Application.Payments.Commands.CreatePayment;
using ErpApp.Application.Payments.Posting;
using ErpApp.Application.Purchasing;
using ErpApp.Application.Purchasing.Commands.ApproveDebitNote;
using ErpApp.Application.Purchasing.Commands.ApproveExpense;
using ErpApp.Application.Purchasing.Commands.ApprovePurchaseBill;
using ErpApp.Application.Purchasing.Commands.CreateDebitNote;
using ErpApp.Application.Purchasing.Commands.CreateExpense;
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
using ErpApp.Domain.Common;
using ErpApp.Domain.Contacts;
using ErpApp.Domain.Payments;
using ErpApp.Domain.Purchasing;
using ErpApp.Domain.Tenancy;

namespace ErpApp.Application.UnitTests.Contacts;

public class ContactStatementQueryHandlerTests
{
    [Fact]
    public async Task Handle_computes_running_balance_for_customer_including_opening_carry_in_and_standalone_reversal()
    {
        var db = TestAppDbContext.Create();
        var seed = await SeedAsync(db);

        // Pre-period Invoice (dated well before FromDate) folds into OpeningBalance alongside
        // Contact.OpeningBalance (seeded at 1000).
        await CreateAndApproveInvoiceAsync(db, seed, new DateOnly(2026, 1, 1), 500m, seed.CustomerId);

        var fromDate = new DateOnly(2026, 2, 1);
        var toDate = new DateOnly(2026, 2, 28);

        var invoiceA = await CreateAndApproveInvoiceAsync(db, seed, new DateOnly(2026, 2, 5), 1000m, seed.CustomerId);
        // Linked reversal -- must match the source line's exact Rate (conversion-cap enforcement),
        // expressed as a fractional Quantity: 0.3 of the 1000-Rate line = 300.
        await CreateAndApproveCreditNoteAsync(db, seed, new DateOnly(2026, 2, 10), 0.3m, 1000m, DocumentType.Invoice, invoiceA.Id);
        // Standalone reversal (no ReferrerId) -- included here unlike ContactAgeingSummaryQuery.
        await CreateAndApproveCreditNoteAsync(db, seed, new DateOnly(2026, 2, 15), 1m, 150m, null, null);
        await CreateAndApprovePaymentAsync(
            db, seed, new DateOnly(2026, 2, 20), PaymentDirection.Received, seed.CustomerId,
            [(DocumentType.Invoice, invoiceA.Id, 250m)]);

        // Excluded: Draft (never approved) and dated after ToDate.
        await CreateInvoiceAsync(db, seed, new DateOnly(2026, 2, 12), 9999m, seed.CustomerId);
        await CreateAndApproveInvoiceAsync(db, seed, new DateOnly(2026, 3, 5), 7777m, seed.CustomerId);

        var query = new ContactStatementQuery(seed.OrganizationId, ContactType.Customer, seed.CustomerId, fromDate, toDate);
        Assert.Equal("Reports.CustomerStatement.View", query.PermissionKey);

        var handler = new ContactStatementQueryHandler(db);
        var result = await handler.Handle(query, CancellationToken.None);

        Assert.Equal(1500m, result.OpeningBalance); // 1000 (Contact.OpeningBalance) + 500 (pre-period Invoice)
        Assert.Equal("DR", result.OpeningBalanceType); // AR-normal, positive balance owed by the customer

        Assert.Equal(4, result.Rows.Count);

        Assert.Equal(DocumentType.Invoice, result.Rows[0].DocumentType);
        Assert.Equal(1000m, result.Rows[0].Debit);
        Assert.Equal(0m, result.Rows[0].Credit);
        Assert.Equal(2500m, result.Rows[0].Balance);
        Assert.Equal("DR", result.Rows[0].BalanceType);

        Assert.Equal(DocumentType.CreditNote, result.Rows[1].DocumentType);
        Assert.Equal(0m, result.Rows[1].Debit);
        Assert.Equal(300m, result.Rows[1].Credit);
        Assert.Equal(2200m, result.Rows[1].Balance);

        Assert.Equal(DocumentType.CreditNote, result.Rows[2].DocumentType);
        Assert.Equal(150m, result.Rows[2].Credit);
        Assert.Equal(2050m, result.Rows[2].Balance);

        Assert.Equal(DocumentType.Payment, result.Rows[3].DocumentType);
        Assert.Equal(250m, result.Rows[3].Credit);
        Assert.Equal(1800m, result.Rows[3].Balance);
        Assert.Equal("DR", result.Rows[3].BalanceType);

        Assert.Equal(1800m, result.ClosingBalance);
        Assert.Equal("DR", result.ClosingBalanceType);
    }

    [Fact]
    public async Task Handle_computes_supplier_balance_net_of_tds_with_ap_normal_polarity_and_flips_to_dr_on_overpayment()
    {
        var db = TestAppDbContext.Create();
        var seed = await SeedAsync(db);

        var fromDate = new DateOnly(2026, 3, 1);
        var toDate = new DateOnly(2026, 3, 31);

        var bill = await CreateAndApprovePurchaseBillAsync(db, seed, new DateOnly(2026, 3, 5), 1000m, seed.TdsTypeId);
        await CreateAndApproveDebitNoteAsync(db, seed, new DateOnly(2026, 3, 8), 0.2m, 1000m, seed.TdsTypeId, bill.Id);
        await CreateAndApproveExpenseAsync(db, seed, new DateOnly(2026, 3, 10), 500m, seed.TdsTypeId);
        // A Payment larger than the net payable flips the running balance negative (an overpayment) --
        // proves BalanceType flips to "DR" for a Supplier once the balance goes negative, the mirror
        // image of the Customer test's always-positive "DR" case.
        await CreateAndApprovePaymentAsync(
            db, seed, new DateOnly(2026, 3, 20), PaymentDirection.Paid, seed.SupplierId,
            [(DocumentType.PurchaseBill, bill.Id, 2000m)]);

        // Excluded: Draft and out-of-range.
        await CreatePurchaseBillAsync(db, seed, seed.SupplierId, new DateOnly(2026, 3, 15), 6000m, null);
        await CreateAndApprovePurchaseBillAsync(db, seed, new DateOnly(2026, 4, 1), 8000m, null);

        var query = new ContactStatementQuery(seed.OrganizationId, ContactType.Supplier, seed.SupplierId, fromDate, toDate);
        Assert.Equal("Reports.SupplierStatement.View", query.PermissionKey);

        var handler = new ContactStatementQueryHandler(db);
        var result = await handler.Handle(query, CancellationToken.None);

        Assert.Equal(0m, result.OpeningBalance);
        Assert.Equal("CR", result.OpeningBalanceType);

        Assert.Equal(4, result.Rows.Count);

        Assert.Equal(DocumentType.PurchaseBill, result.Rows[0].DocumentType);
        Assert.Equal(900m, result.Rows[0].Credit); // 1000 gross - 100 TDS
        Assert.Equal(0m, result.Rows[0].Debit);
        Assert.Equal(900m, result.Rows[0].Balance);
        Assert.Equal("CR", result.Rows[0].BalanceType);

        Assert.Equal(DocumentType.DebitNote, result.Rows[1].DocumentType);
        Assert.Equal(180m, result.Rows[1].Debit); // 200 gross - 20 TDS, reduces the payable
        Assert.Equal(0m, result.Rows[1].Credit);
        Assert.Equal(720m, result.Rows[1].Balance);

        Assert.Equal(DocumentType.Expense, result.Rows[2].DocumentType);
        Assert.Equal(450m, result.Rows[2].Credit); // 500 gross - 50 TDS
        Assert.Equal(1170m, result.Rows[2].Balance);

        Assert.Equal(DocumentType.Payment, result.Rows[3].DocumentType);
        Assert.Equal(2000m, result.Rows[3].Debit);
        Assert.Equal(830m, result.Rows[3].Balance); // |1170 - 2000|
        Assert.Equal("DR", result.Rows[3].BalanceType); // overpaid -- the Supplier now owes us

        Assert.Equal(830m, result.ClosingBalance);
        Assert.Equal("DR", result.ClosingBalanceType);
    }

    [Fact]
    public async Task Handle_throws_not_found_when_contact_type_does_not_match()
    {
        var db = TestAppDbContext.Create();
        var seed = await SeedAsync(db);

        var handler = new ContactStatementQueryHandler(db);

        await Assert.ThrowsAsync<NotFoundException>(() => handler.Handle(
            new ContactStatementQuery(
                seed.OrganizationId, ContactType.Supplier, seed.CustomerId, new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31)),
            CancellationToken.None));
    }

    private sealed record Seed(
        Guid OrganizationId, FakeDocumentNumberGenerator NumberGenerator, Guid CustomerId, Guid SupplierId,
        Guid WarehouseId, Guid ProductId, Guid CashAccountId, Guid TdsTypeId);

    private static async Task<Seed> SeedAsync(IAppDbContext db)
    {
        var organizationId = Guid.NewGuid();
        var numberGenerator = new FakeDocumentNumberGenerator();

        // OpeningBalance=1000 exercises the Customer test's opening-carry-in formula.
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

    private static async Task<CreateInvoiceResult> CreateInvoiceAsync(IAppDbContext db, Seed seed, DateOnly date, decimal rate, Guid contactId)
    {
        return await new CreateInvoiceCommandHandler(db).Handle(
            new CreateInvoiceCommand(
                seed.OrganizationId, contactId, seed.WarehouseId, date, null,
                [new InvoiceLineInput(seed.ProductId, 1m, rate, VatRate.NoVat)]),
            CancellationToken.None);
    }

    private static async Task<(Guid Id, string Code)> CreateAndApproveInvoiceAsync(
        IAppDbContext db, Seed seed, DateOnly date, decimal rate, Guid contactId)
    {
        var created = await CreateInvoiceAsync(db, seed, date, rate, contactId);
        var stockLedgerService = new StockLedgerService(db);
        var approved = await new ApproveInvoiceCommandHandler(
            db, seed.NumberGenerator, new FakeCurrentUserService(Guid.NewGuid()), new InvoicePostingRule(),
            new FifoStockAvailabilityPolicy(db, stockLedgerService), stockLedgerService)
            .Handle(new ApproveInvoiceCommand(seed.OrganizationId, created.Id, OverrideWarning: false), CancellationToken.None);
        return (approved.Id, approved.Code);
    }

    private static async Task<(Guid Id, string Code)> CreateAndApproveCreditNoteAsync(
        IAppDbContext db, Seed seed, DateOnly date, decimal quantity, decimal rate, DocumentType? referrerType, Guid? referrerId)
    {
        var created = await new CreateCreditNoteCommandHandler(db).Handle(
            new CreateCreditNoteCommand(
                seed.OrganizationId, seed.CustomerId, date, null,
                [new CreditNoteLineInput(seed.ProductId, quantity, rate, VatRate.NoVat)], referrerType, referrerId),
            CancellationToken.None);

        var approved = await new ApproveCreditNoteCommandHandler(
            db, seed.NumberGenerator, new FakeCurrentUserService(Guid.NewGuid()),
            new CreditNotePostingRule(), new StockLedgerService(db))
            .Handle(new ApproveCreditNoteCommand(seed.OrganizationId, created.Id), CancellationToken.None);

        return (approved.Id, approved.Code);
    }

    private static async Task<CreatePurchaseBillResult> CreatePurchaseBillAsync(
        IAppDbContext db, Seed seed, Guid supplierId, DateOnly date, decimal rate, Guid? tdsTypeId)
    {
        return await new CreatePurchaseBillCommandHandler(db).Handle(
            new CreatePurchaseBillCommand(
                seed.OrganizationId, supplierId, seed.WarehouseId, date, null, null, false, null, null, null, tdsTypeId,
                [new PurchaseBillLineInput(seed.ProductId, 1m, rate, VatRate.NoVat, ExpenditureClassification.Others)]),
            CancellationToken.None);
    }

    private static async Task<(Guid Id, string Code)> CreateAndApprovePurchaseBillAsync(
        IAppDbContext db, Seed seed, DateOnly date, decimal rate, Guid? tdsTypeId)
    {
        var created = await CreatePurchaseBillAsync(db, seed, seed.SupplierId, date, rate, tdsTypeId);
        var approved = await new ApprovePurchaseBillCommandHandler(
            db, seed.NumberGenerator, new FakeCurrentUserService(Guid.NewGuid()), new PurchaseBillPostingRule(),
            new StockLedgerService(db))
            .Handle(new ApprovePurchaseBillCommand(seed.OrganizationId, created.Id), CancellationToken.None);
        return (approved.Id, approved.Code);
    }

    private static async Task<(Guid Id, string Code)> CreateAndApproveDebitNoteAsync(
        IAppDbContext db, Seed seed, DateOnly date, decimal quantity, decimal rate, Guid? tdsTypeId, Guid referrerPurchaseBillId)
    {
        var created = await new CreateDebitNoteCommandHandler(db).Handle(
            new CreateDebitNoteCommand(
                seed.OrganizationId, seed.SupplierId, date, null, tdsTypeId,
                [new DebitNoteLineInput(seed.ProductId, quantity, rate, VatRate.NoVat)],
                DocumentType.PurchaseBill, referrerPurchaseBillId),
            CancellationToken.None);

        var approved = await new ApproveDebitNoteCommandHandler(
            db, seed.NumberGenerator, new FakeCurrentUserService(Guid.NewGuid()),
            new DebitNotePostingRule(), new StockLedgerService(db))
            .Handle(new ApproveDebitNoteCommand(seed.OrganizationId, created.Id), CancellationToken.None);

        return (approved.Id, approved.Code);
    }

    private static async Task<(Guid Id, string Code)> CreateAndApproveExpenseAsync(
        IAppDbContext db, Seed seed, DateOnly date, decimal amount, Guid? tdsTypeId)
    {
        var created = await new CreateExpenseCommandHandler(db).Handle(
            new CreateExpenseCommand(
                seed.OrganizationId, seed.SupplierId, date, null, null, null, tdsTypeId is not null, tdsTypeId,
                [new ExpenseLineInput(seed.CashAccountId, amount, VatRate.NoVat)]),
            CancellationToken.None);

        var approved = await new ApproveExpenseCommandHandler(
            db, seed.NumberGenerator, new FakeCurrentUserService(Guid.NewGuid()), new ExpensePostingRule())
            .Handle(new ApproveExpenseCommand(seed.OrganizationId, created.Id), CancellationToken.None);

        return (approved.Id, approved.Code);
    }

    private static async Task<(Guid Id, string Code)> CreateAndApprovePaymentAsync(
        IAppDbContext db, Seed seed, DateOnly date, PaymentDirection direction, Guid contactId,
        IReadOnlyList<(DocumentType TargetType, Guid TargetId, decimal Amount)> allocations)
    {
        var amount = allocations.Sum(a => a.Amount);
        var created = await new CreatePaymentCommandHandler(db).Handle(
            new CreatePaymentCommand(
                seed.OrganizationId, contactId, direction, date, null, seed.CashAccountId, amount, null,
                allocations.Select(a => new PaymentAllocationInput(a.TargetType, a.TargetId, a.Amount)).ToList()),
            CancellationToken.None);

        var approved = await new ApprovePaymentCommandHandler(
            db, seed.NumberGenerator, new FakeCurrentUserService(Guid.NewGuid()), new PaymentPostingRule())
            .Handle(new ApprovePaymentCommand(seed.OrganizationId, created.Id), CancellationToken.None);

        return (approved.Id, approved.Code);
    }
}
