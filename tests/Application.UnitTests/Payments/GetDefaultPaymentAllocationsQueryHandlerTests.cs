using ErpApp.Application.Accounting;
using ErpApp.Application.Accounting.Commands.CreateAccount;
using ErpApp.Application.Accounting.Commands.CreateAccountGroup;
using ErpApp.Application.Catalog.Commands.CreateProduct;
using ErpApp.Application.Catalog.Commands.CreateProductCategory;
using ErpApp.Application.Catalog.Commands.CreateUnitOfMeasurement;
using ErpApp.Application.Common.Persistence;
using ErpApp.Application.Configuration.Commands.CreateTdsType;
using ErpApp.Application.Contacts.Commands.CreateContact;
using ErpApp.Application.Inventory.Stock;
using ErpApp.Application.Payments;
using ErpApp.Application.Payments.Commands.ApprovePayment;
using ErpApp.Application.Payments.Commands.CreatePayment;
using ErpApp.Application.Payments.Posting;
using ErpApp.Application.Payments.Queries.GetDefaultPaymentAllocations;
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
using ErpApp.Domain.Common;
using ErpApp.Domain.Contacts;
using ErpApp.Domain.Payments;
using ErpApp.Domain.Purchasing;
using ErpApp.Domain.Tenancy;

namespace ErpApp.Application.UnitTests.Payments;

/// <summary>
/// First unit tests for this handler (phase-9-status.md scope decision #7 flagged the TDS gap; fixed
/// in Phase 11 alongside a reversal-netting gap the fix itself raised -- see phase-11-status.md).
/// </summary>
public class GetDefaultPaymentAllocationsQueryHandlerTests
{
    [Fact]
    public async Task Handle_nets_tds_off_purchase_bills_and_orders_fifo_oldest_first()
    {
        var db = TestAppDbContext.Create();
        var seed = await SeedAsync(db);

        // Older bill: gross 1000, TDS 10% -> net outstanding 900.
        var olderBill = await CreateAndApprovePurchaseBillAsync(db, seed, new DateOnly(2026, 1, 1), 1000m, seed.TdsTypeId);
        // Newer bill: gross 500, no TDS -> net outstanding 500 (regression guard: unaffected by the TDS fix).
        var newerBill = await CreateAndApprovePurchaseBillAsync(db, seed, new DateOnly(2026, 2, 1), 500m, null);

        var handler = new GetDefaultPaymentAllocationsQueryHandler(db);
        var result = await handler.Handle(
            new GetDefaultPaymentAllocationsQuery(seed.OrganizationId, seed.SupplierId, 1200m, PaymentDirection.Paid),
            CancellationToken.None);

        Assert.Equal(2, result.Count);
        Assert.Equal(olderBill.Id, result[0].TargetDocumentId);
        Assert.Equal(900m, result[0].Amount); // fully consumes the TDS-net outstanding first (FIFO oldest)
        Assert.Equal(newerBill.Id, result[1].TargetDocumentId);
        Assert.Equal(300m, result[1].Amount); // remaining 1200 - 900 = 300 of the 500 outstanding
    }

    [Fact]
    public async Task Handle_reduces_purchase_bill_outstanding_by_a_prior_approved_payment_allocation()
    {
        var db = TestAppDbContext.Create();
        var seed = await SeedAsync(db);

        // Gross 1000, TDS 10% -> net 900. A prior Approved Payment already allocated 300 against it.
        var bill = await CreateAndApprovePurchaseBillAsync(db, seed, new DateOnly(2026, 1, 1), 1000m, seed.TdsTypeId);
        await CreateAndApprovePaymentAsync(
            db, seed, new DateOnly(2026, 1, 15), PaymentDirection.Paid, seed.SupplierId,
            [(DocumentType.PurchaseBill, bill.Id, 300m)]);

        var handler = new GetDefaultPaymentAllocationsQueryHandler(db);
        var result = await handler.Handle(
            new GetDefaultPaymentAllocationsQuery(seed.OrganizationId, seed.SupplierId, 1000m, PaymentDirection.Paid),
            CancellationToken.None);

        var suggestion = Assert.Single(result);
        Assert.Equal(bill.Id, suggestion.TargetDocumentId);
        Assert.Equal(600m, suggestion.Amount); // 900 net - 300 already allocated
    }

    [Fact]
    public async Task Handle_reduces_purchase_bill_outstanding_by_a_linked_debit_note_net_of_its_own_tds()
    {
        var db = TestAppDbContext.Create();
        var seed = await SeedAsync(db);

        // Gross 1000, TDS 10% -> net 900. A linked DebitNote reverses 0.2 of the same 1000-Rate line
        // (exact-Rate match per the conversion-cap enforcement, phase-6-status.md bug #4) -- gross 200,
        // its own TDS 10% -> net reversal 180.
        var bill = await CreateAndApprovePurchaseBillAsync(db, seed, new DateOnly(2026, 1, 1), 1000m, seed.TdsTypeId);
        await CreateAndApproveDebitNoteAsync(db, seed, new DateOnly(2026, 1, 10), 0.2m, 1000m, seed.TdsTypeId, bill.Id);

        var handler = new GetDefaultPaymentAllocationsQueryHandler(db);
        var result = await handler.Handle(
            new GetDefaultPaymentAllocationsQuery(seed.OrganizationId, seed.SupplierId, 1000m, PaymentDirection.Paid),
            CancellationToken.None);

        var suggestion = Assert.Single(result);
        Assert.Equal(bill.Id, suggestion.TargetDocumentId);
        Assert.Equal(720m, suggestion.Amount); // 900 net - 180 net reversal
    }

    [Fact]
    public async Task Handle_reduces_invoice_outstanding_by_a_linked_credit_note()
    {
        var db = TestAppDbContext.Create();
        var seed = await SeedAsync(db);

        // Gross 1000 (no TDS concept on the Sales side). A linked CreditNote reverses 0.3 of the same
        // 1000-Rate line -- gross 300.
        var invoice = await CreateAndApproveInvoiceAsync(db, seed, new DateOnly(2026, 1, 1), 1000m);
        await CreateAndApproveCreditNoteAsync(db, seed, new DateOnly(2026, 1, 10), 0.3m, 1000m, invoice.Id);

        var handler = new GetDefaultPaymentAllocationsQueryHandler(db);
        var result = await handler.Handle(
            new GetDefaultPaymentAllocationsQuery(seed.OrganizationId, seed.CustomerId, 1000m, PaymentDirection.Received),
            CancellationToken.None);

        var suggestion = Assert.Single(result);
        Assert.Equal(invoice.Id, suggestion.TargetDocumentId);
        Assert.Equal(700m, suggestion.Amount); // 1000 gross - 300 linked reversal
    }

    private sealed record Seed(
        Guid OrganizationId, FakeDocumentNumberGenerator NumberGenerator, Guid CustomerId, Guid SupplierId,
        Guid WarehouseId, Guid ProductId, Guid CashAccountId, Guid TdsTypeId);

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
        IAppDbContext db, Seed seed, DateOnly date, decimal rate)
    {
        var created = await new CreateInvoiceCommandHandler(db).Handle(
            new CreateInvoiceCommand(
                seed.OrganizationId, seed.CustomerId, seed.WarehouseId, date, null,
                [new InvoiceLineInput(seed.ProductId, 1m, rate, VatRate.NoVat)]),
            CancellationToken.None);

        var stockLedgerService = new StockLedgerService(db);
        var approved = await new ApproveInvoiceCommandHandler(
            db, seed.NumberGenerator, new FakeCurrentUserService(Guid.NewGuid()), new InvoicePostingRule(),
            new FifoStockAvailabilityPolicy(db, stockLedgerService), stockLedgerService)
            .Handle(new ApproveInvoiceCommand(seed.OrganizationId, created.Id, OverrideWarning: false), CancellationToken.None);

        return (approved.Id, approved.Code);
    }

    private static async Task<(Guid Id, string Code)> CreateAndApproveCreditNoteAsync(
        IAppDbContext db, Seed seed, DateOnly date, decimal quantity, decimal rate, Guid referrerInvoiceId)
    {
        var created = await new CreateCreditNoteCommandHandler(db).Handle(
            new CreateCreditNoteCommand(
                seed.OrganizationId, seed.CustomerId, date, null,
                [new CreditNoteLineInput(seed.ProductId, quantity, rate, VatRate.NoVat)], DocumentType.Invoice, referrerInvoiceId),
            CancellationToken.None);

        var approved = await new ApproveCreditNoteCommandHandler(
            db, seed.NumberGenerator, new FakeCurrentUserService(Guid.NewGuid()),
            new CreditNotePostingRule(), new StockLedgerService(db))
            .Handle(new ApproveCreditNoteCommand(seed.OrganizationId, created.Id), CancellationToken.None);

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
