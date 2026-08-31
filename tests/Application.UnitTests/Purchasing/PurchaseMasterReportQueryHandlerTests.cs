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
using ErpApp.Application.Purchasing;
using ErpApp.Application.Purchasing.Commands.ApproveDebitNote;
using ErpApp.Application.Purchasing.Commands.ApprovePurchaseBill;
using ErpApp.Application.Purchasing.Commands.CreateDebitNote;
using ErpApp.Application.Purchasing.Commands.CreatePurchaseBill;
using ErpApp.Application.Purchasing.Posting;
using ErpApp.Application.Purchasing.Queries.PurchaseMasterReport;
using ErpApp.Application.Tenancy.Commands.CreateWarehouse;
using ErpApp.Application.UnitTests.TestSupport;
using ErpApp.Domain.Accounting;
using ErpApp.Domain.Catalog;
using ErpApp.Domain.Common;
using ErpApp.Domain.Contacts;
using ErpApp.Domain.Purchasing;
using ErpApp.Domain.Tenancy;

namespace ErpApp.Application.UnitTests.Purchasing;

public class PurchaseMasterReportQueryHandlerTests
{
    [Fact]
    public async Task Handle_returns_one_row_per_line_for_approved_purchase_bills_and_debit_notes_within_range_only()
    {
        var db = TestAppDbContext.Create();
        var seed = await SeedAsync(db);

        var billInRange = await CreateAndApprovePurchaseBillAsync(
            db, seed, new DateOnly(2026, 1, 15), 10m, 100m, VatRate.ThirteenPercentVat);
        var draftBill = await CreatePurchaseBillAsync(db, seed, new DateOnly(2026, 1, 20), 5m, 50m, VatRate.NoVat);
        var billOutOfRange = await CreateAndApprovePurchaseBillAsync(
            db, seed, new DateOnly(2026, 3, 1), 2m, 20m, VatRate.NoVat);

        var debitNote = await CreateAndApproveStandaloneDebitNoteAsync(db, seed, new DateOnly(2026, 1, 18), 1m, 40m);

        var handler = new PurchaseMasterReportQueryHandler(db);
        var result = await handler.Handle(
            new PurchaseMasterReportQuery(seed.OrganizationId, new DateOnly(2026, 1, 1), new DateOnly(2026, 1, 31), null, null, null),
            CancellationToken.None);

        Assert.Equal(2, result.Rows.Count);

        var billRow = Assert.Single(result.Rows, r => r.Type == DocumentType.PurchaseBill);
        Assert.Equal(billInRange.Code, billRow.EntryNo);
        Assert.Equal(seed.SupplierId, billRow.ContactId);
        Assert.Equal(seed.ContactGroupId, billRow.ContactGroupId);
        Assert.Equal(seed.ContactGroupName, billRow.ContactGroupName);
        Assert.Equal(seed.WarehouseId, billRow.WarehouseId);
        Assert.Equal(seed.ProductId, billRow.ProductId);
        Assert.Equal(10m, billRow.Quantity);
        Assert.Equal(100m, billRow.Rate);
        Assert.Equal(1000m, billRow.Amount);
        Assert.Equal(130m, billRow.VatAmount);
        Assert.Equal(1130m, billRow.TotalAmount);

        var debitNoteRow = Assert.Single(result.Rows, r => r.Type == DocumentType.DebitNote);
        Assert.Equal(debitNote.Code, debitNoteRow.EntryNo);
        Assert.Null(debitNoteRow.WarehouseId);

        Assert.DoesNotContain(result.Rows, r => r.EntryNo == draftBill.Code);
        Assert.DoesNotContain(result.Rows, r => r.EntryNo == billOutOfRange.Code);
    }

    [Fact]
    public async Task Handle_filters_by_contact_and_product()
    {
        var db = TestAppDbContext.Create();
        var seed = await SeedAsync(db);

        var otherSupplier = await new CreateContactCommandHandler(db, new FakeDocumentNumberGenerator()).Handle(
            new CreateContactCommand(seed.OrganizationId, ContactType.Supplier, "Other Supplier", null, null, null, null, null, 0m),
            CancellationToken.None);
        var otherProduct = await CreateServiceProductAsync(db, seed, "Other Service");

        var billForSeedSupplier = await CreateAndApprovePurchaseBillAsync(
            db, seed, new DateOnly(2026, 1, 10), 1m, 100m, VatRate.NoVat);

        var createdForOtherSupplier = await new CreatePurchaseBillCommandHandler(db).Handle(
            new CreatePurchaseBillCommand(
                seed.OrganizationId, otherSupplier.Id, seed.WarehouseId, new DateOnly(2026, 1, 11), null, null, false, null, null,
                null, null, [new PurchaseBillLineInput(otherProduct, 1m, 200m, VatRate.NoVat, ExpenditureClassification.Others)]),
            CancellationToken.None);
        var billForOtherSupplier = await ApprovePurchaseBillAsync(db, seed, createdForOtherSupplier.Id);

        var handler = new PurchaseMasterReportQueryHandler(db);

        var byContact = await handler.Handle(
            new PurchaseMasterReportQuery(
                seed.OrganizationId, new DateOnly(2026, 1, 1), new DateOnly(2026, 1, 31), seed.SupplierId, null, null),
            CancellationToken.None);
        var contactRow = Assert.Single(byContact.Rows);
        Assert.Equal(billForSeedSupplier.Code, contactRow.EntryNo);

        var byProduct = await handler.Handle(
            new PurchaseMasterReportQuery(
                seed.OrganizationId, new DateOnly(2026, 1, 1), new DateOnly(2026, 1, 31), null, otherProduct, null),
            CancellationToken.None);
        var productRow = Assert.Single(byProduct.Rows);
        Assert.Equal(billForOtherSupplier.Code, productRow.EntryNo);
    }

    [Fact]
    public async Task Handle_filters_by_warehouse_and_resolves_debit_note_warehouse_from_its_source_purchase_bill()
    {
        var db = TestAppDbContext.Create();
        var seed = await SeedAsync(db);

        // Phase 20f: a second warehouse needs the MultipleWarehouses entitlement.
        await TenantFeatureSeed.SeedAllFeaturesEnabledAsync(db, seed.OrganizationId);

        var otherWarehouse = await new CreateWarehouseCommandHandler(db).Handle(
            new CreateWarehouseCommand(seed.OrganizationId, "Other Warehouse"), CancellationToken.None);

        var bill = await CreateAndApprovePurchaseBillAsync(db, seed, new DateOnly(2026, 1, 10), 10m, 100m, VatRate.NoVat);

        var debitNote = await new CreateDebitNoteCommandHandler(db).Handle(
            new CreateDebitNoteCommand(
                seed.OrganizationId, seed.SupplierId, new DateOnly(2026, 1, 12), null, null,
                [new DebitNoteLineInput(seed.ProductId, 3m, 100m, VatRate.NoVat)],
                DocumentType.PurchaseBill, bill.Id),
            CancellationToken.None);
        await new ApproveDebitNoteCommandHandler(
            db, seed.NumberGenerator, new FakeCurrentUserService(Guid.NewGuid()),
            new DebitNotePostingRule(), new StockLedgerService(db))
            .Handle(new ApproveDebitNoteCommand(seed.OrganizationId, debitNote.Id), CancellationToken.None);

        var handler = new PurchaseMasterReportQueryHandler(db);

        var byOriginalWarehouse = await handler.Handle(
            new PurchaseMasterReportQuery(
                seed.OrganizationId, new DateOnly(2026, 1, 1), new DateOnly(2026, 1, 31), null, null, seed.WarehouseId),
            CancellationToken.None);
        Assert.Equal(2, byOriginalWarehouse.Rows.Count);
        var resolvedDebitNoteRow = Assert.Single(byOriginalWarehouse.Rows, r => r.Type == DocumentType.DebitNote);
        Assert.Equal(seed.WarehouseId, resolvedDebitNoteRow.WarehouseId);
        Assert.Equal(seed.WarehouseName, resolvedDebitNoteRow.WarehouseName);

        var byOtherWarehouse = await handler.Handle(
            new PurchaseMasterReportQuery(
                seed.OrganizationId, new DateOnly(2026, 1, 1), new DateOnly(2026, 1, 31), null, null, otherWarehouse.Id),
            CancellationToken.None);
        Assert.Empty(byOtherWarehouse.Rows);
    }

    // See SalesMasterReportQueryHandlerTests' matching comment: a single FakeDocumentNumberGenerator
    // instance is shared across every Create/Approve call in one test, or every approved document
    // collides on the same "Foo-0001" code.
    private sealed record Seed(
        Guid OrganizationId, FakeDocumentNumberGenerator NumberGenerator, Guid SupplierId, Guid ContactGroupId,
        string ContactGroupName, Guid WarehouseId, string WarehouseName, Guid ProductId, Guid CategoryId, Guid UnitId,
        Guid PurchaseAccountId, Guid AccountsPayableId, Guid VatReceivableAccountId);

    private static async Task<Seed> SeedAsync(IAppDbContext db)
    {
        var organizationId = Guid.NewGuid();
        var numberGenerator = new FakeDocumentNumberGenerator();

        var contactGroup = await new CreateContactGroupCommandHandler(db).Handle(
            new CreateContactGroupCommand(organizationId, "Wholesale Suppliers", null), CancellationToken.None);

        var supplier = await new CreateContactCommandHandler(db, numberGenerator).Handle(
            new CreateContactCommand(
                organizationId, ContactType.Supplier, "Global Supplies", null, null, null, null, contactGroup.Id, 0m),
            CancellationToken.None);

        var warehouse = await new CreateWarehouseCommandHandler(db).Handle(
            new CreateWarehouseCommand(organizationId, "Main Warehouse"), CancellationToken.None);

        var category = await new CreateProductCategoryCommandHandler(db).Handle(
            new CreateProductCategoryCommand(organizationId, "Services", null), CancellationToken.None);
        var unit = await new CreateUnitOfMeasurementCommandHandler(db).Handle(
            new CreateUnitOfMeasurementCommand(organizationId, "Hour", "hr"), CancellationToken.None);

        var product = await new CreateProductCommandHandler(db, numberGenerator).Handle(
            new CreateProductCommand(
                organizationId, ProductType.Service, "Maintenance", category.Id, unit.Id, null, true, 100m, 80m,
                VatRate.ThirteenPercentVat, 0, false),
            CancellationToken.None);

        var assetGroup = await new CreateAccountGroupCommandHandler(db).Handle(
            new CreateAccountGroupCommand(organizationId, "Current Assets", AccountRootType.Asset, null), CancellationToken.None);
        var liabilityGroup = await new CreateAccountGroupCommandHandler(db).Handle(
            new CreateAccountGroupCommand(organizationId, "Current Liabilities", AccountRootType.Liability, null), CancellationToken.None);
        var expenseGroup = await new CreateAccountGroupCommandHandler(db).Handle(
            new CreateAccountGroupCommand(organizationId, "Operating Expenses", AccountRootType.Expense, null), CancellationToken.None);

        var vatReceivable = await new CreateAccountCommandHandler(db, numberGenerator).Handle(
            new CreateAccountCommand(organizationId, "VAT Receivable", assetGroup.Id), CancellationToken.None);
        var ap = await new CreateAccountCommandHandler(db, numberGenerator).Handle(
            new CreateAccountCommand(organizationId, "Accounts Payable", liabilityGroup.Id), CancellationToken.None);
        var purchase = await new CreateAccountCommandHandler(db, numberGenerator).Handle(
            new CreateAccountCommand(organizationId, "Purchase Expense", expenseGroup.Id), CancellationToken.None);

        var settings = TenantSettings.CreateDefault(organizationId);
        settings.SetAccountingDefaults(null, null, null, purchase.Id, ap.Id, vatReceivable.Id, null);
        db.TenantSettings.Add(settings);
        await db.SaveChangesAsync(CancellationToken.None);

        return new Seed(
            organizationId, numberGenerator, supplier.Id, contactGroup.Id, contactGroup.Name, warehouse.Id, warehouse.Name,
            product.Id, category.Id, unit.Id, purchase.Id, ap.Id, vatReceivable.Id);
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

    private static async Task<CreatePurchaseBillResult> CreatePurchaseBillAsync(
        IAppDbContext db, Seed seed, DateOnly date, decimal quantity, decimal rate, VatRate vatRate)
    {
        return await new CreatePurchaseBillCommandHandler(db).Handle(
            new CreatePurchaseBillCommand(
                seed.OrganizationId, seed.SupplierId, seed.WarehouseId, date, null, null, false, null, null, null, null,
                [new PurchaseBillLineInput(seed.ProductId, quantity, rate, vatRate, ExpenditureClassification.Others)]),
            CancellationToken.None);
    }

    private static async Task<ApprovePurchaseBillResult> ApprovePurchaseBillAsync(
        IAppDbContext db, Seed seed, Guid purchaseBillId)
    {
        return await new ApprovePurchaseBillCommandHandler(
            db, seed.NumberGenerator, new FakeCurrentUserService(Guid.NewGuid()), new PurchaseBillPostingRule(),
            new StockLedgerService(db))
            .Handle(new ApprovePurchaseBillCommand(seed.OrganizationId, purchaseBillId), CancellationToken.None);
    }

    /// <summary>Returns the post-Approve code -- CreatePurchaseBillCommandHandler's own result
    /// still carries PurchaseBill.DraftCode ("DRAFT"), same "number assigned at Approve, not
    /// Create" reasoning as Sales.CreateInvoiceCommandHandler.</summary>
    private static async Task<(Guid Id, string Code)> CreateAndApprovePurchaseBillAsync(
        IAppDbContext db, Seed seed, DateOnly date, decimal quantity, decimal rate, VatRate vatRate)
    {
        var created = await CreatePurchaseBillAsync(db, seed, date, quantity, rate, vatRate);
        var approved = await ApprovePurchaseBillAsync(db, seed, created.Id);
        return (approved.Id, approved.Code);
    }

    private static async Task<(Guid Id, string Code)> CreateAndApproveStandaloneDebitNoteAsync(
        IAppDbContext db, Seed seed, DateOnly date, decimal quantity, decimal rate)
    {
        var created = await new CreateDebitNoteCommandHandler(db).Handle(
            new CreateDebitNoteCommand(
                seed.OrganizationId, seed.SupplierId, date, null, null,
                [new DebitNoteLineInput(seed.ProductId, quantity, rate, VatRate.NoVat)]),
            CancellationToken.None);

        var approved = await new ApproveDebitNoteCommandHandler(
            db, seed.NumberGenerator, new FakeCurrentUserService(Guid.NewGuid()),
            new DebitNotePostingRule(), new StockLedgerService(db))
            .Handle(new ApproveDebitNoteCommand(seed.OrganizationId, created.Id), CancellationToken.None);

        return (approved.Id, approved.Code);
    }
}
