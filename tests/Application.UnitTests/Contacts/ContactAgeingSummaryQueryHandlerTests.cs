using ErpApp.Application.Accounting;
using ErpApp.Application.Accounting.Commands.CreateAccount;
using ErpApp.Application.Accounting.Commands.CreateAccountGroup;
using ErpApp.Application.Catalog.Commands.CreateProduct;
using ErpApp.Application.Catalog.Commands.CreateProductCategory;
using ErpApp.Application.Catalog.Commands.CreateUnitOfMeasurement;
using ErpApp.Application.Common.Persistence;
using ErpApp.Application.Configuration.Commands.CreateTdsType;
using ErpApp.Application.Contacts.Commands.CreateContact;
using ErpApp.Application.Contacts.Commands.CreateContactGroup;
using ErpApp.Application.Contacts.Queries.ContactAgeingSummary;
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

public class ContactAgeingSummaryQueryHandlerTests
{
    [Fact]
    public async Task Handle_buckets_customer_invoices_by_age_and_nets_linked_and_unlinked_reversals_and_payments()
    {
        var db = TestAppDbContext.Create();
        var seed = await SeedAsync(db);
        var asOf = new DateOnly(2026, 5, 1);

        // Bucket boundaries -- exactly on each edge, one Invoice per bucket at a distinct Rate so
        // each is independently identifiable in the result.
        await CreateAndApproveInvoiceAsync(db, seed, asOf.AddDays(-30), 100m); // age 30 -> 1-30
        await CreateAndApproveInvoiceAsync(db, seed, asOf.AddDays(-31), 200m); // age 31 -> 31-60
        await CreateAndApproveInvoiceAsync(db, seed, asOf.AddDays(-60), 50m); // age 60 -> 31-60
        await CreateAndApproveInvoiceAsync(db, seed, asOf.AddDays(-61), 300m); // age 61 -> 61-90
        await CreateAndApproveInvoiceAsync(db, seed, asOf.AddDays(-90), 25m); // age 90 -> 61-90
        await CreateAndApproveInvoiceAsync(db, seed, asOf.AddDays(-91), 400m); // age 91 -> 91+

        // A linked CreditNote (ReferrerId -> this Invoice) partially reduces that specific bill's own
        // bucket. The conversion-cap enforcement (phase-6-status.md bug #4) requires an exact
        // (ProductId, Rate, VatRate) match against the source line, so the reversal is expressed as a
        // fractional Quantity at the *same* Rate (0.4 of the 1000-Rate line = 400), not a different Rate.
        var linkedInvoice = await CreateAndApproveInvoiceAsync(db, seed, asOf.AddDays(-91), 1000m, seed.CustomerId);
        await CreateAndApproveCreditNoteAsync(db, seed, asOf.AddDays(-80), 0.4m, 1000m, DocumentType.Invoice, linkedInvoice.Id);

        // A Payment allocation partially reduces another bill's bucket.
        var paidInvoice = await CreateAndApproveInvoiceAsync(db, seed, asOf.AddDays(-45), 1000m, seed.CustomerId);
        await CreateAndApprovePaymentAsync(
            db, seed, asOf.AddDays(-10), PaymentDirection.Received, seed.CustomerId,
            [(DocumentType.Invoice, paidInvoice.Id, 600m)]);

        // An Invoice fully offset by a linked CreditNote -- outstanding nets to exactly zero, must be
        // excluded from every bucket entirely (not shown as a zero row).
        var settledInvoice = await CreateAndApproveInvoiceAsync(db, seed, asOf.AddDays(-15), 500m, seed.CustomerId);
        await CreateAndApproveCreditNoteAsync(db, seed, asOf.AddDays(-5), 1m, 500m, DocumentType.Invoice, settledInvoice.Id);

        // A standalone CreditNote (no ReferrerId) reduces the Contact's real balance but has no bill
        // of its own to bucket against -- excluded from Ageing's bucket totals by design (still
        // included in ContactStatementQuery's flat ledger, see that test file).
        await CreateAndApproveCreditNoteAsync(db, seed, asOf.AddDays(-5), 1m, 999m, null, null);

        // A Draft Invoice must never contribute -- Approved-only.
        await CreateInvoiceAsync(db, seed, asOf.AddDays(-5), 5000m, seed.CustomerId);

        var query = new ContactAgeingSummaryQuery(seed.OrganizationId, ContactType.Customer, asOf);
        Assert.Equal("Reports.CustomerAgeingSummary.View", query.PermissionKey);

        var handler = new ContactAgeingSummaryQueryHandler(db);
        var result = await handler.Handle(query, CancellationToken.None);

        var row = Assert.Single(result.Rows, r => r.ContactId == seed.CustomerId);
        Assert.Equal(100m, row.Days1To30);
        // 200 + 50 (boundary) + 400 (paidInvoice's remainder, 1000 - 600 allocated, age 45 -> this bucket)
        Assert.Equal(650m, row.Days31To60);
        Assert.Equal(325m, row.Days61To90); // 300 + 25
        // 400 (boundary, age 91) + 600 (linkedInvoice's remainder, 1000 - 400 linked CN, age 91)
        Assert.Equal(1000m, row.Days91Plus);
        Assert.Equal(row.Days1To30 + row.Days31To60 + row.Days61To90 + row.Days91Plus, row.Total);
    }

    [Fact]
    public async Task Handle_computes_supplier_outstanding_net_of_tds_and_filters_by_contact_group()
    {
        var db = TestAppDbContext.Create();
        var seed = await SeedAsync(db);
        var asOf = new DateOnly(2026, 5, 1);

        // PurchaseBill: gross 1000, TDS 100 -> NetAmount 900. A linked DebitNote reverses gross 200
        // (0.2 of the 1000-Rate line, same conversion-cap exact-Rate-match reasoning as the Customer
        // test), TDS 20 -> net 180, reducing outstanding by 180 (not 200). A Payment allocation then
        // reduces the remainder by 300.
        var bill = await CreateAndApprovePurchaseBillAsync(db, seed, asOf.AddDays(-45), 1000m, seed.TdsTypeId);
        await CreateAndApproveDebitNoteAsync(db, seed, asOf.AddDays(-40), 0.2m, 1000m, seed.TdsTypeId, bill.Id);
        await CreateAndApprovePaymentAsync(
            db, seed, asOf.AddDays(-10), PaymentDirection.Paid, seed.SupplierId,
            [(DocumentType.PurchaseBill, bill.Id, 300m)]);
        // Expected outstanding: 900 - 180 - 300 = 420, all in the 31-60 bucket (age 45).

        // An Expense creates a payable too (net of its own TDS) but can never be reduced by any
        // Payment allocation or DebitNote in this codebase's data model (PaymentValidation only
        // recognizes Invoice/PurchaseBill as allocation targets, and DebitNote only ever converts
        // from PurchaseBill) -- it stays fully outstanding forever.
        await CreateAndApproveExpenseAsync(db, seed, asOf.AddDays(-91), 500m, seed.TdsTypeId); // gross 500, TDS 50 -> 450 net, age 91+

        // A second Supplier outside the ContactGroup filter -- must be excluded when ContactGroupId
        // is supplied, even though it has real outstanding activity.
        var outsideSupplier = await CreateSupplierAsync(db, seed, "Outside Group Supplier", null);
        await CreateAndApprovePurchaseBillAsync(db, seed, outsideSupplier, asOf.AddDays(-10), 777m, null);

        var query = new ContactAgeingSummaryQuery(seed.OrganizationId, ContactType.Supplier, asOf, seed.SupplierGroupId);
        Assert.Equal("Reports.SupplierAgeingSummary.View", query.PermissionKey);

        var handler = new ContactAgeingSummaryQueryHandler(db);
        var result = await handler.Handle(query, CancellationToken.None);

        var row = Assert.Single(result.Rows);
        Assert.Equal(seed.SupplierId, row.ContactId);
        Assert.Equal("Suppliers", row.ContactGroupName);
        Assert.Equal(420m, row.Days31To60);
        Assert.Equal(450m, row.Days91Plus);
        Assert.Equal(0m, row.Days1To30);
        Assert.Equal(0m, row.Days61To90);
    }

    private sealed record Seed(
        Guid OrganizationId, FakeDocumentNumberGenerator NumberGenerator, Guid CustomerId, Guid SupplierId,
        Guid SupplierGroupId, Guid WarehouseId, Guid ProductId, Guid CashAccountId, Guid TdsTypeId);

    private static async Task<Seed> SeedAsync(IAppDbContext db)
    {
        var organizationId = Guid.NewGuid();
        var numberGenerator = new FakeDocumentNumberGenerator();

        var customer = await new CreateContactCommandHandler(db, numberGenerator).Handle(
            new CreateContactCommand(organizationId, ContactType.Customer, "Acme Traders", null, null, null, null, null, 0m),
            CancellationToken.None);

        var supplierGroup = await new CreateContactGroupCommandHandler(db).Handle(
            new CreateContactGroupCommand(organizationId, "Suppliers", null), CancellationToken.None);
        var supplier = await new CreateContactCommandHandler(db, numberGenerator).Handle(
            new CreateContactCommand(
                organizationId, ContactType.Supplier, "Global Supplies", null, null, null, null, supplierGroup.Id, 0m),
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

        return new Seed(
            organizationId, numberGenerator, customer.Id, supplier.Id, supplierGroup.Id, warehouse.Id, product.Id,
            cash.Id, tdsType.Id);
    }

    private static async Task<Guid> CreateSupplierAsync(IAppDbContext db, Seed seed, string name, Guid? groupId)
    {
        var supplier = await new CreateContactCommandHandler(db, seed.NumberGenerator).Handle(
            new CreateContactCommand(seed.OrganizationId, ContactType.Supplier, name, null, null, null, null, groupId, 0m),
            CancellationToken.None);
        return supplier.Id;
    }

    private static async Task<CreateInvoiceResult> CreateInvoiceAsync(
        IAppDbContext db, Seed seed, DateOnly date, decimal rate, Guid contactId)
    {
        return await new CreateInvoiceCommandHandler(db).Handle(
            new CreateInvoiceCommand(
                seed.OrganizationId, contactId, seed.WarehouseId, date, null,
                [new InvoiceLineInput(seed.ProductId, 1m, rate, VatRate.NoVat)]),
            CancellationToken.None);
    }

    private static async Task<(Guid Id, string Code)> CreateAndApproveInvoiceAsync(
        IAppDbContext db, Seed seed, DateOnly date, decimal rate, Guid? contactId = null)
    {
        var created = await CreateInvoiceAsync(db, seed, date, rate, contactId ?? seed.CustomerId);
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

    private static async Task<(Guid Id, string Code)> CreateAndApprovePurchaseBillAsync(
        IAppDbContext db, Seed seed, DateOnly date, decimal rate, Guid? tdsTypeId)
        => await CreateAndApprovePurchaseBillAsync(db, seed, seed.SupplierId, date, rate, tdsTypeId);

    private static async Task<(Guid Id, string Code)> CreateAndApprovePurchaseBillAsync(
        IAppDbContext db, Seed seed, Guid supplierId, DateOnly date, decimal rate, Guid? tdsTypeId)
    {
        var created = await new CreatePurchaseBillCommandHandler(db).Handle(
            new CreatePurchaseBillCommand(
                seed.OrganizationId, supplierId, seed.WarehouseId, date, null, null, false, null, null, null, tdsTypeId,
                [new PurchaseBillLineInput(seed.ProductId, 1m, rate, VatRate.NoVat, ExpenditureClassification.Others)]),
            CancellationToken.None);

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
