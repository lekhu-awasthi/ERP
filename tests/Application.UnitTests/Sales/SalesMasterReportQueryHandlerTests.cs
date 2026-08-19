using ErpApp.Application.Accounting;
using ErpApp.Application.Accounting.Commands.CreateAccount;
using ErpApp.Application.Accounting.Commands.CreateAccountGroup;
using ErpApp.Application.Accounting.Posting;
using ErpApp.Application.Catalog.Commands.CreateProduct;
using ErpApp.Application.Catalog.Commands.CreateProductCategory;
using ErpApp.Application.Catalog.Commands.CreateUnitOfMeasurement;
using ErpApp.Application.Common.Persistence;
using ErpApp.Application.Contacts.Commands.CreateContact;
using ErpApp.Application.Contacts.Commands.CreateContactGroup;
using ErpApp.Application.Inventory.Stock;
using ErpApp.Application.Sales;
using ErpApp.Application.Sales.Commands.ApproveCreditNote;
using ErpApp.Application.Sales.Commands.ApproveInvoice;
using ErpApp.Application.Sales.Commands.CreateCreditNote;
using ErpApp.Application.Sales.Commands.CreateInvoice;
using ErpApp.Application.Sales.Posting;
using ErpApp.Application.Sales.Queries.SalesMasterReport;
using ErpApp.Application.Sales.Stock;
using ErpApp.Application.Tenancy.Commands.CreateWarehouse;
using ErpApp.Application.UnitTests.TestSupport;
using ErpApp.Domain.Accounting;
using ErpApp.Domain.Catalog;
using ErpApp.Domain.Common;
using ErpApp.Domain.Contacts;
using ErpApp.Domain.Sales;
using ErpApp.Domain.Tenancy;

namespace ErpApp.Application.UnitTests.Sales;

public class SalesMasterReportQueryHandlerTests
{
    [Fact]
    public async Task Handle_returns_one_row_per_line_for_approved_invoices_and_credit_notes_within_range_only()
    {
        var db = TestAppDbContext.Create();
        var seed = await SeedAsync(db);

        var invoiceInRange = await CreateAndApproveInvoiceAsync(
            db, seed, new DateOnly(2026, 1, 15), 10m, 100m, VatRate.ThirteenPercentVat);
        var draftInvoice = await CreateInvoiceAsync(db, seed, new DateOnly(2026, 1, 20), 5m, 50m, VatRate.NoVat);
        var invoiceOutOfRange = await CreateAndApproveInvoiceAsync(
            db, seed, new DateOnly(2026, 3, 1), 2m, 20m, VatRate.NoVat);

        var creditNote = await CreateAndApproveStandaloneCreditNoteAsync(db, seed, new DateOnly(2026, 1, 18), 1m, 40m);

        var handler = new SalesMasterReportQueryHandler(db);
        var result = await handler.Handle(
            new SalesMasterReportQuery(seed.OrganizationId, new DateOnly(2026, 1, 1), new DateOnly(2026, 1, 31), null, null, null),
            CancellationToken.None);

        Assert.Equal(2, result.Rows.Count);

        var invoiceRow = Assert.Single(result.Rows, r => r.Type == DocumentType.Invoice);
        Assert.Equal(invoiceInRange.Code, invoiceRow.EntryNo);
        Assert.Equal(seed.CustomerId, invoiceRow.ContactId);
        Assert.Equal(seed.ContactGroupId, invoiceRow.ContactGroupId);
        Assert.Equal(seed.ContactGroupName, invoiceRow.ContactGroupName);
        Assert.Equal(seed.WarehouseId, invoiceRow.WarehouseId);
        Assert.Equal(seed.ProductId, invoiceRow.ProductId);
        Assert.Equal(10m, invoiceRow.Quantity);
        Assert.Equal(100m, invoiceRow.Rate);
        Assert.Equal(1000m, invoiceRow.Amount);
        Assert.Equal(130m, invoiceRow.VatAmount);
        Assert.Equal(1130m, invoiceRow.TotalAmount);

        var creditNoteRow = Assert.Single(result.Rows, r => r.Type == DocumentType.CreditNote);
        Assert.Equal(creditNote.Code, creditNoteRow.EntryNo);
        Assert.Null(creditNoteRow.WarehouseId);

        Assert.DoesNotContain(result.Rows, r => r.EntryNo == draftInvoice.Code);
        Assert.DoesNotContain(result.Rows, r => r.EntryNo == invoiceOutOfRange.Code);
    }

    /// <summary>Phase 16b: ItemDiscount/TransactionDiscount/NetSales are reconstructed from the
    /// line's own Quantity/Rate/DiscountPct plus the stored (already fully-netted) Amount -- not
    /// separately stored fields. Qty 10 * Rate 100 = 1,000 gross, 10% line discount -> ItemDiscount
    /// 100, NetAfterLineDiscount 900 (the "Amount" column, matching the live per-line Amount cell
    /// which a header discount never touches), 5% header discount -> TransactionDiscount 45,
    /// NetSales 855, VAT 13% of 855 = 111.15.</summary>
    [Fact]
    public async Task Handle_computes_item_discount_transaction_discount_and_net_sales_columns()
    {
        var db = TestAppDbContext.Create();
        var seed = await SeedAsync(db);

        var created = await new CreateInvoiceCommandHandler(db).Handle(
            new CreateInvoiceCommand(
                seed.OrganizationId, seed.CustomerId, seed.WarehouseId, new DateOnly(2026, 1, 10), null,
                [new InvoiceLineInput(seed.ProductId, 10m, 100m, VatRate.ThirteenPercentVat, DiscountPct: 10)],
                DiscountPct: 5),
            CancellationToken.None);
        await ApproveInvoiceAsync(db, seed, created.Id);

        var handler = new SalesMasterReportQueryHandler(db);
        var result = await handler.Handle(
            new SalesMasterReportQuery(seed.OrganizationId, new DateOnly(2026, 1, 1), new DateOnly(2026, 1, 31), null, null, null),
            CancellationToken.None);

        var row = Assert.Single(result.Rows);
        Assert.Equal(900m, row.Amount);
        Assert.Equal(100m, row.ItemDiscount);
        Assert.Equal(45m, row.TransactionDiscount);
        Assert.Equal(855m, row.NetSales);
        Assert.Equal(111.15m, row.VatAmount);
        Assert.Equal(966.15m, row.TotalAmount);
    }

    [Fact]
    public async Task Handle_filters_by_contact_and_product()
    {
        var db = TestAppDbContext.Create();
        var seed = await SeedAsync(db);

        var otherCustomer = await new CreateContactCommandHandler(db, new FakeDocumentNumberGenerator()).Handle(
            new CreateContactCommand(seed.OrganizationId, ContactType.Customer, "Other Customer", null, null, null, null, null, 0m),
            CancellationToken.None);
        var otherProduct = await CreateServiceProductAsync(db, seed, "Other Service");

        var invoiceForSeedCustomer = await CreateAndApproveInvoiceAsync(
            db, seed, new DateOnly(2026, 1, 10), 1m, 100m, VatRate.NoVat);

        var createdForOtherCustomer = await new CreateInvoiceCommandHandler(db).Handle(
            new CreateInvoiceCommand(
                seed.OrganizationId, otherCustomer.Id, seed.WarehouseId, new DateOnly(2026, 1, 11), null,
                [new InvoiceLineInput(otherProduct, 1m, 200m, VatRate.NoVat)]),
            CancellationToken.None);
        var invoiceForOtherCustomer = await ApproveInvoiceAsync(db, seed, createdForOtherCustomer.Id);

        var handler = new SalesMasterReportQueryHandler(db);

        var byContact = await handler.Handle(
            new SalesMasterReportQuery(
                seed.OrganizationId, new DateOnly(2026, 1, 1), new DateOnly(2026, 1, 31), seed.CustomerId, null, null),
            CancellationToken.None);
        var contactRow = Assert.Single(byContact.Rows);
        Assert.Equal(invoiceForSeedCustomer.Code, contactRow.EntryNo);

        var byProduct = await handler.Handle(
            new SalesMasterReportQuery(
                seed.OrganizationId, new DateOnly(2026, 1, 1), new DateOnly(2026, 1, 31), null, otherProduct, null),
            CancellationToken.None);
        var productRow = Assert.Single(byProduct.Rows);
        Assert.Equal(invoiceForOtherCustomer.Code, productRow.EntryNo);
    }

    [Fact]
    public async Task Handle_filters_by_warehouse_and_resolves_credit_note_warehouse_from_its_source_invoice()
    {
        var db = TestAppDbContext.Create();
        var seed = await SeedAsync(db);

        var otherWarehouse = await new CreateWarehouseCommandHandler(db).Handle(
            new CreateWarehouseCommand(seed.OrganizationId, "Other Warehouse"), CancellationToken.None);

        var invoice = await CreateAndApproveInvoiceAsync(db, seed, new DateOnly(2026, 1, 10), 10m, 100m, VatRate.NoVat);

        var creditNote = await new CreateCreditNoteCommandHandler(db).Handle(
            new CreateCreditNoteCommand(
                seed.OrganizationId, seed.CustomerId, new DateOnly(2026, 1, 12), null,
                [new CreditNoteLineInput(seed.ProductId, 3m, 100m, VatRate.NoVat)],
                DocumentType.Invoice, invoice.Id),
            CancellationToken.None);
        await new ApproveCreditNoteCommandHandler(
            db, seed.NumberGenerator, new FakeCurrentUserService(Guid.NewGuid()),
            new CreditNotePostingRule(), new StockLedgerService(db))
            .Handle(new ApproveCreditNoteCommand(seed.OrganizationId, creditNote.Id), CancellationToken.None);

        var handler = new SalesMasterReportQueryHandler(db);

        var byOriginalWarehouse = await handler.Handle(
            new SalesMasterReportQuery(
                seed.OrganizationId, new DateOnly(2026, 1, 1), new DateOnly(2026, 1, 31), null, null, seed.WarehouseId),
            CancellationToken.None);
        Assert.Equal(2, byOriginalWarehouse.Rows.Count);
        var resolvedCreditNoteRow = Assert.Single(byOriginalWarehouse.Rows, r => r.Type == DocumentType.CreditNote);
        Assert.Equal(seed.WarehouseId, resolvedCreditNoteRow.WarehouseId);
        Assert.Equal(seed.WarehouseName, resolvedCreditNoteRow.WarehouseName);

        var byOtherWarehouse = await handler.Handle(
            new SalesMasterReportQuery(
                seed.OrganizationId, new DateOnly(2026, 1, 1), new DateOnly(2026, 1, 31), null, null, otherWarehouse.Id),
            CancellationToken.None);
        Assert.Empty(byOtherWarehouse.Rows);
    }

    // A single FakeDocumentNumberGenerator instance is shared across every Create/Approve call
    // within one test (via Seed.NumberGenerator) -- FakeDocumentNumberGenerator's counter starts
    // fresh at 1 per instance, so a fresh `new FakeDocumentNumberGenerator()` per Approve call
    // makes every approved document collide on the same code ("Invoice-0001" for every invoice),
    // which silently defeats any assertion that compares two different documents' EntryNo values.
    private sealed record Seed(
        Guid OrganizationId, FakeDocumentNumberGenerator NumberGenerator, Guid CustomerId, Guid ContactGroupId,
        string ContactGroupName, Guid WarehouseId, string WarehouseName, Guid ProductId, Guid CategoryId, Guid UnitId,
        Guid SalesAccountId, Guid AccountsReceivableId, Guid VatPayableAccountId);

    private static async Task<Seed> SeedAsync(IAppDbContext db)
    {
        var organizationId = Guid.NewGuid();
        var numberGenerator = new FakeDocumentNumberGenerator();

        var contactGroup = await new CreateContactGroupCommandHandler(db).Handle(
            new CreateContactGroupCommand(organizationId, "Retail Customers", null), CancellationToken.None);

        var customer = await new CreateContactCommandHandler(db, numberGenerator).Handle(
            new CreateContactCommand(
                organizationId, ContactType.Customer, "Acme Traders", null, null, null, null, contactGroup.Id, 0m),
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

        var ar = await new CreateAccountCommandHandler(db, numberGenerator).Handle(
            new CreateAccountCommand(organizationId, "Accounts Receivable", assetGroup.Id), CancellationToken.None);
        var vatPayable = await new CreateAccountCommandHandler(db, numberGenerator).Handle(
            new CreateAccountCommand(organizationId, "VAT Payable", liabilityGroup.Id), CancellationToken.None);
        var sales = await new CreateAccountCommandHandler(db, numberGenerator).Handle(
            new CreateAccountCommand(organizationId, "Sales Revenue", incomeGroup.Id), CancellationToken.None);

        var settings = TenantSettings.CreateDefault(organizationId);
        settings.SetAccountingDefaults(sales.Id, ar.Id, vatPayable.Id, null, null, null, null);
        db.TenantSettings.Add(settings);
        await db.SaveChangesAsync(CancellationToken.None);

        return new Seed(
            organizationId, numberGenerator, customer.Id, contactGroup.Id, contactGroup.Name, warehouse.Id, warehouse.Name,
            product.Id, category.Id, unit.Id, sales.Id, ar.Id, vatPayable.Id);
    }

    private static async Task<Guid> CreateServiceProductAsync(IAppDbContext db, Seed seed, string name)
    {
        var product = await new CreateProductCommandHandler(db, seed.NumberGenerator).Handle(
            new CreateProductCommand(
                seed.OrganizationId, ProductType.Service, name, seed.CategoryId, seed.UnitId, null, true, 100m, 80m,
                VatRate.NoVat, 0, false),
            CancellationToken.None);
        return product.Id;
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

    private static async Task<ApproveInvoiceResult> ApproveInvoiceAsync(IAppDbContext db, Seed seed, Guid invoiceId)
    {
        var stockLedgerService = new StockLedgerService(db);
        return await new ApproveInvoiceCommandHandler(
            db, seed.NumberGenerator, new FakeCurrentUserService(Guid.NewGuid()), new InvoicePostingRule(),
            new FifoStockAvailabilityPolicy(db, stockLedgerService), stockLedgerService)
            .Handle(new ApproveInvoiceCommand(seed.OrganizationId, invoiceId, OverrideWarning: false), CancellationToken.None);
    }

    /// <summary>Returns the post-Approve code -- CreateInvoiceCommandHandler's own result still
    /// carries Invoice.DraftCode ("DRAFT"), since the real number is only assigned inside
    /// Approve() (architecture-spec.md §3.1, confirmed live per CLAUDE.md's "document numbers are
    /// assigned at Approve, not at Create").</summary>
    private static async Task<(Guid Id, string Code)> CreateAndApproveInvoiceAsync(
        IAppDbContext db, Seed seed, DateOnly date, decimal quantity, decimal rate, VatRate vatRate)
    {
        var created = await CreateInvoiceAsync(db, seed, date, quantity, rate, vatRate);
        var approved = await ApproveInvoiceAsync(db, seed, created.Id);
        return (approved.Id, approved.Code);
    }

    private static async Task<(Guid Id, string Code)> CreateAndApproveStandaloneCreditNoteAsync(
        IAppDbContext db, Seed seed, DateOnly date, decimal quantity, decimal rate)
    {
        var created = await new CreateCreditNoteCommandHandler(db).Handle(
            new CreateCreditNoteCommand(
                seed.OrganizationId, seed.CustomerId, date, null,
                [new CreditNoteLineInput(seed.ProductId, quantity, rate, VatRate.NoVat)]),
            CancellationToken.None);

        var approved = await new ApproveCreditNoteCommandHandler(
            db, seed.NumberGenerator, new FakeCurrentUserService(Guid.NewGuid()),
            new CreditNotePostingRule(), new StockLedgerService(db))
            .Handle(new ApproveCreditNoteCommand(seed.OrganizationId, created.Id), CancellationToken.None);

        return (approved.Id, approved.Code);
    }
}
