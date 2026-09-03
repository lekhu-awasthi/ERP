using ErpApp.Application.Accounting;
using ErpApp.Application.Accounting.Commands.ApproveJournalVoucher;
using ErpApp.Application.Accounting.Commands.CreateAccount;
using ErpApp.Application.Accounting.Commands.CreateAccountGroup;
using ErpApp.Application.Accounting.Commands.CreateJournalVoucher;
using ErpApp.Application.Accounting.Posting;
using ErpApp.Application.Catalog.Commands.CreateProduct;
using ErpApp.Application.Catalog.Commands.CreateProductCategory;
using ErpApp.Application.Catalog.Commands.CreateUnitOfMeasurement;
using ErpApp.Application.Common.Persistence;
using ErpApp.Application.Contacts.Commands.CreateContact;
using ErpApp.Application.Contacts.Commands.CreateContactGroup;
using ErpApp.Application.Inventory.Stock;
using ErpApp.Application.Payments;
using ErpApp.Application.Payments.Commands.ApprovePayment;
using ErpApp.Application.Payments.Commands.CreatePayment;
using ErpApp.Application.Payments.Posting;
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
using ErpApp.Domain.Accounting;
using ErpApp.Domain.Catalog;
using ErpApp.Domain.Common;
using ErpApp.Domain.Contacts;
using ErpApp.Domain.Payments;
using ErpApp.Domain.Purchasing;
using ErpApp.Domain.Tenancy;

namespace ErpApp.Application.UnitTests.TestSupport;

/// <summary>
/// Shared seeding for phase-26b's report tests. Everything is created through the <b>real</b>
/// Create/Approve command handlers rather than by inserting aggregates directly -- phase-8b's rule,
/// so the documents under test carry real numbers, real GL postings and real conversion caps, and
/// a report that only works against hand-built rows fails here.
///
/// <para>Deliberately a separate helper from
/// <c>ContactAgeingSummaryQueryHandlerTests</c>'s private seed: that one is tuned to bucket
/// boundaries, and this one has to reach Journal Vouchers, product categories and a second product,
/// which that scenario has no use for.</para>
/// </summary>
internal sealed record TradeReportSeed(
    Guid OrganizationId,
    FakeDocumentNumberGenerator NumberGenerator,
    Guid CustomerId,
    Guid SecondCustomerId,
    Guid SupplierId,
    Guid CustomerGroupId,
    Guid WarehouseId,
    Guid CategoryId,
    Guid SecondCategoryId,
    Guid ProductId,
    Guid VatProductId,
    Guid SecondProductId,
    Guid ArAccountId,
    Guid ApAccountId,
    Guid CashAccountId)
{
    public static async Task<TradeReportSeed> CreateAsync(IAppDbContext db, decimal customerOpeningBalance = 0m)
    {
        var organizationId = Guid.NewGuid();
        var numbers = new FakeDocumentNumberGenerator();

        var customerGroup = await new CreateContactGroupCommandHandler(db).Handle(
            new CreateContactGroupCommand(organizationId, "Key Accounts", null), CancellationToken.None);

        var customer = await new CreateContactCommandHandler(db, numbers).Handle(
            new CreateContactCommand(
                organizationId, ContactType.Customer, "Acme Traders", null, "PAN-111", null, null,
                customerGroup.Id, customerOpeningBalance),
            CancellationToken.None);

        var secondCustomer = await new CreateContactCommandHandler(db, numbers).Handle(
            new CreateContactCommand(
                organizationId, ContactType.Customer, "Beacon Retail", null, null, null, null, null, 0m),
            CancellationToken.None);

        var supplier = await new CreateContactCommandHandler(db, numbers).Handle(
            new CreateContactCommand(
                organizationId, ContactType.Supplier, "Global Supplies", null, null, null, null, null, 0m),
            CancellationToken.None);

        var warehouse = await new CreateWarehouseCommandHandler(db).Handle(
            new CreateWarehouseCommand(organizationId, "Main Warehouse"), CancellationToken.None);

        var category = await new CreateProductCategoryCommandHandler(db).Handle(
            new CreateProductCategoryCommand(organizationId, "Services", null), CancellationToken.None);
        var secondCategory = await new CreateProductCategoryCommandHandler(db).Handle(
            new CreateProductCategoryCommand(organizationId, "Consumables", null), CancellationToken.None);
        var unit = await new CreateUnitOfMeasurementCommandHandler(db).Handle(
            new CreateUnitOfMeasurementCommand(organizationId, "Piece", "pc"), CancellationToken.None);

        // Every product is a Service so no test needs opening stock to approve an Invoice
        // (phase-8c's Goods-product gotcha).
        var product = await new CreateProductCommandHandler(db, numbers).Handle(
            new CreateProductCommand(
                organizationId, ProductType.Service, "Consulting", category.Id, unit.Id, null, true, 150m, 100m,
                VatRate.NoVat, 0, false),
            CancellationToken.None);

        var vatProduct = await new CreateProductCommandHandler(db, numbers).Handle(
            new CreateProductCommand(
                organizationId, ProductType.Service, "Taxable Support", category.Id, unit.Id, null, true, 150m, 100m,
                VatRate.ThirteenPercentVat, 0, false),
            CancellationToken.None);

        var secondProduct = await new CreateProductCommandHandler(db, numbers).Handle(
            new CreateProductCommand(
                organizationId, ProductType.Service, "Cleaning", secondCategory.Id, unit.Id, null, true, 80m, 50m,
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

        var ar = await CreateAccountAsync(db, numbers, organizationId, "Accounts Receivable", assetGroup.Id);
        var cash = await CreateAccountAsync(db, numbers, organizationId, "Cash", assetGroup.Id);
        var inventory = await CreateAccountAsync(db, numbers, organizationId, "Inventory", assetGroup.Id);
        var vatPayable = await CreateAccountAsync(db, numbers, organizationId, "VAT Payable", liabilityGroup.Id);
        var ap = await CreateAccountAsync(db, numbers, organizationId, "Accounts Payable", liabilityGroup.Id);
        var tdsPayable = await CreateAccountAsync(db, numbers, organizationId, "TDS Payable", liabilityGroup.Id);
        var vatReceivable = await CreateAccountAsync(db, numbers, organizationId, "VAT Receivable", assetGroup.Id);
        var sales = await CreateAccountAsync(db, numbers, organizationId, "Sales Revenue", incomeGroup.Id);
        var purchase = await CreateAccountAsync(db, numbers, organizationId, "Purchase Expense", expenseGroup.Id);
        var cogs = await CreateAccountAsync(db, numbers, organizationId, "Cost of Goods Sold", expenseGroup.Id);

        var settings = TenantSettings.CreateDefault(organizationId);
        settings.SetAccountingDefaults(sales.Id, ar.Id, vatPayable.Id, purchase.Id, ap.Id, vatReceivable.Id, tdsPayable.Id);
        settings.SetInventoryDefaults(inventory.Id, cogs.Id, null, null);
        db.TenantSettings.Add(settings);
        await db.SaveChangesAsync(CancellationToken.None);

        return new TradeReportSeed(
            organizationId, numbers, customer.Id, secondCustomer.Id, supplier.Id, customerGroup.Id,
            warehouse.Id, category.Id, secondCategory.Id, product.Id, vatProduct.Id, secondProduct.Id,
            ar.Id, ap.Id, cash.Id);
    }

    private static Task<CreateAccountResult> CreateAccountAsync(
        IAppDbContext db, FakeDocumentNumberGenerator numbers, Guid organizationId, string name, Guid groupId) =>
        new CreateAccountCommandHandler(db, numbers).Handle(
            new CreateAccountCommand(organizationId, name, groupId), CancellationToken.None);

    public async Task<(Guid Id, string Code)> ApproveInvoiceAsync(
        IAppDbContext db, DateOnly date, decimal rate, Guid? contactId = null,
        Guid? productId = null, decimal quantity = 1m, decimal discountPct = 0m)
    {
        var created = await new CreateInvoiceCommandHandler(db).Handle(
            new CreateInvoiceCommand(
                this.OrganizationId, contactId ?? this.CustomerId, this.WarehouseId, date, null,
                [new InvoiceLineInput(productId ?? this.ProductId, quantity, rate, VatRate.NoVat, discountPct)]),
            CancellationToken.None);

        var stock = new StockLedgerService(db);
        var approved = await new ApproveInvoiceCommandHandler(
                db, this.NumberGenerator, new FakeCurrentUserService(Guid.NewGuid()), new InvoicePostingRule(),
                new FifoStockAvailabilityPolicy(db, stock), stock)
            .Handle(new ApproveInvoiceCommand(this.OrganizationId, created.Id, OverrideWarning: false), CancellationToken.None);

        return (approved.Id, approved.Code);
    }

    public async Task<(Guid Id, string Code)> ApproveVatInvoiceAsync(IAppDbContext db, DateOnly date, decimal rate)
    {
        var created = await new CreateInvoiceCommandHandler(db).Handle(
            new CreateInvoiceCommand(
                this.OrganizationId, this.CustomerId, this.WarehouseId, date, null,
                [new InvoiceLineInput(this.VatProductId, 1m, rate, VatRate.ThirteenPercentVat)]),
            CancellationToken.None);

        var stock = new StockLedgerService(db);
        var approved = await new ApproveInvoiceCommandHandler(
                db, this.NumberGenerator, new FakeCurrentUserService(Guid.NewGuid()), new InvoicePostingRule(),
                new FifoStockAvailabilityPolicy(db, stock), stock)
            .Handle(new ApproveInvoiceCommand(this.OrganizationId, created.Id, OverrideWarning: false), CancellationToken.None);

        return (approved.Id, approved.Code);
    }

    public async Task<(Guid Id, string Code)> ApproveCreditNoteAsync(
        IAppDbContext db, DateOnly date, decimal quantity, decimal rate, Guid? referrerInvoiceId = null,
        Guid? productId = null, Guid? contactId = null)
    {
        var created = await new CreateCreditNoteCommandHandler(db).Handle(
            new CreateCreditNoteCommand(
                this.OrganizationId, contactId ?? this.CustomerId, date, null,
                [new CreditNoteLineInput(productId ?? this.ProductId, quantity, rate, VatRate.NoVat)],
                referrerInvoiceId is null ? null : DocumentType.Invoice, referrerInvoiceId),
            CancellationToken.None);

        var approved = await new ApproveCreditNoteCommandHandler(
                db, this.NumberGenerator, new FakeCurrentUserService(Guid.NewGuid()),
                new CreditNotePostingRule(), new StockLedgerService(db))
            .Handle(new ApproveCreditNoteCommand(this.OrganizationId, created.Id), CancellationToken.None);

        return (approved.Id, approved.Code);
    }

    public async Task<(Guid Id, string Code)> ApprovePurchaseBillAsync(
        IAppDbContext db, DateOnly date, decimal rate, Guid? productId = null, decimal quantity = 1m)
    {
        var created = await new CreatePurchaseBillCommandHandler(db).Handle(
            new CreatePurchaseBillCommand(
                this.OrganizationId, this.SupplierId, this.WarehouseId, date, null, null, false, null, null, null, null,
                [new PurchaseBillLineInput(
                    productId ?? this.ProductId, quantity, rate, VatRate.NoVat, ExpenditureClassification.Others)]),
            CancellationToken.None);

        var approved = await new ApprovePurchaseBillCommandHandler(
                db, this.NumberGenerator, new FakeCurrentUserService(Guid.NewGuid()), new PurchaseBillPostingRule(),
                new StockLedgerService(db))
            .Handle(new ApprovePurchaseBillCommand(this.OrganizationId, created.Id), CancellationToken.None);

        return (approved.Id, approved.Code);
    }

    public async Task<(Guid Id, string Code)> ApproveDebitNoteAsync(
        IAppDbContext db, DateOnly date, decimal quantity, decimal rate, Guid referrerPurchaseBillId, Guid? productId = null)
    {
        var created = await new CreateDebitNoteCommandHandler(db).Handle(
            new CreateDebitNoteCommand(
                this.OrganizationId, this.SupplierId, date, null, null,
                [new DebitNoteLineInput(productId ?? this.ProductId, quantity, rate, VatRate.NoVat)],
                DocumentType.PurchaseBill, referrerPurchaseBillId),
            CancellationToken.None);

        var approved = await new ApproveDebitNoteCommandHandler(
                db, this.NumberGenerator, new FakeCurrentUserService(Guid.NewGuid()),
                new DebitNotePostingRule(), new StockLedgerService(db))
            .Handle(new ApproveDebitNoteCommand(this.OrganizationId, created.Id), CancellationToken.None);

        return (approved.Id, approved.Code);
    }

    /// <summary>A contact-tagged Journal Voucher: <paramref name="amount"/> debited to the contact's
    /// control account and credited to Cash when <paramref name="debitContact"/>, mirrored when
    /// not.</summary>
    public async Task<(Guid Id, string Code)> ApproveContactJournalVoucherAsync(
        IAppDbContext db, DateOnly date, Guid contactId, Guid controlAccountId, decimal amount, bool debitContact)
    {
        var created = await new CreateJournalVoucherCommandHandler(db).Handle(
            new CreateJournalVoucherCommand(
                this.OrganizationId, date, "JV-REF",
                debitContact
                    ?
                    [
                        new JournalVoucherLineInput(controlAccountId, amount, 0m, contactId),
                        new JournalVoucherLineInput(this.CashAccountId, 0m, amount),
                    ]
                    :
                    [
                        new JournalVoucherLineInput(this.CashAccountId, amount, 0m),
                        new JournalVoucherLineInput(controlAccountId, 0m, amount, contactId),
                    ]),
            CancellationToken.None);

        var approved = await new ApproveJournalVoucherCommandHandler(
                db, this.NumberGenerator, new FakeCurrentUserService(Guid.NewGuid()), new JournalVoucherPostingRule())
            .Handle(new ApproveJournalVoucherCommand(this.OrganizationId, created.Id), CancellationToken.None);

        return (approved.Id, approved.Code);
    }

    public async Task<(Guid Id, string Code)> ApprovePaymentAsync(
        IAppDbContext db, DateOnly date, PaymentDirection direction, Guid contactId,
        IReadOnlyList<(DocumentType TargetType, Guid TargetId, decimal Amount)> allocations)
    {
        var amount = allocations.Sum(a => a.Amount);
        var created = await new CreatePaymentCommandHandler(db).Handle(
            new CreatePaymentCommand(
                this.OrganizationId, contactId, direction, date, null, this.CashAccountId, amount, null,
                [.. allocations.Select(a => new PaymentAllocationInput(a.TargetType, a.TargetId, a.Amount))]),
            CancellationToken.None);

        var approved = await new ApprovePaymentCommandHandler(
                db, this.NumberGenerator, new FakeCurrentUserService(Guid.NewGuid()), new PaymentPostingRule())
            .Handle(new ApprovePaymentCommand(this.OrganizationId, created.Id), CancellationToken.None);

        return (approved.Id, approved.Code);
    }
}
