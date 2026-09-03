using ErpApp.Application.Accounting.Commands.CreateAccount;
using ErpApp.Application.Accounting.Commands.CreateAccountGroup;
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
using ErpApp.Domain.Accounting;
using ErpApp.Domain.Catalog;
using ErpApp.Domain.Common;
using ErpApp.Domain.Contacts;
using ErpApp.Domain.Purchasing;
using ErpApp.Domain.Tenancy;

namespace ErpApp.Application.UnitTests.TestSupport;

/// <summary>
/// One tenant with the accounts, warehouse, customer, supplier and two products phase-26c's
/// inventory, return-register and analytics reports all need, plus helpers that drive real
/// Create/Approve handlers so every stock movement and GL line these reports read is one the
/// application actually wrote. Same approach as phase-8b's report suites: seeding through the
/// handlers rather than by hand is the only way a report test proves anything about the product.
/// </summary>
internal static class InventoryReportSeed
{
    internal sealed record Seed(
        Guid OrganizationId,
        FakeDocumentNumberGenerator NumberGenerator,
        Guid CustomerId,
        Guid SupplierId,
        Guid WarehouseId,
        Guid CategoryId,
        Guid ProductId,
        Guid SecondProductId);

    internal static async Task<Seed> CreateAsync(IAppDbContext db)
    {
        var organizationId = Guid.NewGuid();
        var numberGenerator = new FakeDocumentNumberGenerator();

        var customer = await new CreateContactCommandHandler(db, numberGenerator).Handle(
            new CreateContactCommand(
                organizationId, ContactType.Customer, "Acme Retail", null, "301234567", null, null, null, 0m),
            CancellationToken.None);
        var supplier = await new CreateContactCommandHandler(db, numberGenerator).Handle(
            new CreateContactCommand(
                organizationId, ContactType.Supplier, "Global Supplies", null, "609876543", null, null, null, 0m),
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
        var secondProduct = await new CreateProductCommandHandler(db, numberGenerator).Handle(
            new CreateProductCommand(
                organizationId, ProductType.Goods, "Gadget", category.Id, unit.Id, null, true, 90m, 60m,
                VatRate.NoVat, 0, true),
            CancellationToken.None);

        var assetGroup = await new CreateAccountGroupCommandHandler(db).Handle(
            new CreateAccountGroupCommand(organizationId, "Current Assets", AccountRootType.Asset, null),
            CancellationToken.None);
        var liabilityGroup = await new CreateAccountGroupCommandHandler(db).Handle(
            new CreateAccountGroupCommand(organizationId, "Current Liabilities", AccountRootType.Liability, null),
            CancellationToken.None);
        var expenseGroup = await new CreateAccountGroupCommandHandler(db).Handle(
            new CreateAccountGroupCommand(organizationId, "Operating Expenses", AccountRootType.Expense, null),
            CancellationToken.None);
        var incomeGroup = await new CreateAccountGroupCommandHandler(db).Handle(
            new CreateAccountGroupCommand(organizationId, "Revenue", AccountRootType.Income, null),
            CancellationToken.None);

        var sales = await CreateAccountAsync(db, numberGenerator, organizationId, "Sales", incomeGroup.Id);
        var receivable = await CreateAccountAsync(db, numberGenerator, organizationId, "Accounts Receivable", assetGroup.Id);
        var vatPayable = await CreateAccountAsync(db, numberGenerator, organizationId, "VAT Payable", liabilityGroup.Id);
        var vatReceivable = await CreateAccountAsync(db, numberGenerator, organizationId, "VAT Receivable", assetGroup.Id);
        var payable = await CreateAccountAsync(db, numberGenerator, organizationId, "Accounts Payable", liabilityGroup.Id);
        var purchase = await CreateAccountAsync(db, numberGenerator, organizationId, "Purchase Expense", expenseGroup.Id);
        var inventory = await CreateAccountAsync(db, numberGenerator, organizationId, "Inventory", assetGroup.Id);
        var cogs = await CreateAccountAsync(db, numberGenerator, organizationId, "Cost of Goods Sold", expenseGroup.Id);

        var settings = TenantSettings.CreateDefault(organizationId);
        settings.SetAccountingDefaults(
            sales.Id, receivable.Id, vatPayable.Id, purchase.Id, payable.Id, vatReceivable.Id, null);
        settings.SetInventoryDefaults(inventory.Id, cogs.Id, null, null);
        db.TenantSettings.Add(settings);
        await db.SaveChangesAsync(CancellationToken.None);

        return new Seed(
            organizationId, numberGenerator, customer.Id, supplier.Id, warehouse.Id,
            category.Id, product.Id, secondProduct.Id);
    }

    /// <summary>Stock in, at a known unit cost.</summary>
    internal static async Task<(Guid Id, string Code)> PurchaseAsync(
        IAppDbContext db, Seed seed, DateOnly date, decimal quantity, decimal rate,
        Guid? productId = null, VatRate vatRate = VatRate.NoVat,
        ExpenditureClassification classification = ExpenditureClassification.Others, bool isImport = false)
    {
        var created = await new CreatePurchaseBillCommandHandler(db).Handle(
            new CreatePurchaseBillCommand(
                seed.OrganizationId, seed.SupplierId, seed.WarehouseId, date, null, null, isImport, null, null, null, null,
                [new PurchaseBillLineInput(productId ?? seed.ProductId, quantity, rate, vatRate, classification)]),
            CancellationToken.None);

        var approved = await new ApprovePurchaseBillCommandHandler(
                db, seed.NumberGenerator, new FakeCurrentUserService(Guid.NewGuid()), new PurchaseBillPostingRule(),
                new StockLedgerService(db))
            .Handle(new ApprovePurchaseBillCommand(seed.OrganizationId, created.Id), CancellationToken.None);

        return (approved.Id, approved.Code);
    }

    /// <summary>Stock out.</summary>
    internal static async Task<(Guid Id, string Code)> SellAsync(
        IAppDbContext db, Seed seed, DateOnly date, decimal quantity, decimal rate,
        Guid? productId = null, VatRate vatRate = VatRate.NoVat)
    {
        var created = await new CreateInvoiceCommandHandler(db).Handle(
            new CreateInvoiceCommand(
                seed.OrganizationId, seed.CustomerId, seed.WarehouseId, date, null,
                [new InvoiceLineInput(productId ?? seed.ProductId, quantity, rate, vatRate)]),
            CancellationToken.None);

        var stockLedgerService = new StockLedgerService(db);
        var approved = await new ApproveInvoiceCommandHandler(
                db, seed.NumberGenerator, new FakeCurrentUserService(Guid.NewGuid()), new InvoicePostingRule(),
                new FifoStockAvailabilityPolicy(db, stockLedgerService), stockLedgerService)
            .Handle(
                new ApproveInvoiceCommand(seed.OrganizationId, created.Id, OverrideWarning: true),
                CancellationToken.None);

        return (approved.Id, approved.Code);
    }

    /// <summary>A sales return: stock back in, against the invoice it reverses.</summary>
    internal static async Task<(Guid Id, string Code)> CreditNoteAsync(
        IAppDbContext db, Seed seed, DateOnly date, decimal quantity, decimal rate,
        Guid? sourceInvoiceId = null, Guid? productId = null, VatRate vatRate = VatRate.NoVat)
    {
        var created = await new CreateCreditNoteCommandHandler(db).Handle(
            new CreateCreditNoteCommand(
                seed.OrganizationId, seed.CustomerId, date, null,
                [new CreditNoteLineInput(productId ?? seed.ProductId, quantity, rate, vatRate)],
                sourceInvoiceId is null ? null : DocumentType.Invoice,
                sourceInvoiceId),
            CancellationToken.None);

        var approved = await new ApproveCreditNoteCommandHandler(
                db, seed.NumberGenerator, new FakeCurrentUserService(Guid.NewGuid()), new CreditNotePostingRule(),
                new StockLedgerService(db))
            .Handle(new ApproveCreditNoteCommand(seed.OrganizationId, created.Id), CancellationToken.None);

        return (approved.Id, approved.Code);
    }

    /// <summary>A purchase return: stock back out to the supplier.</summary>
    internal static async Task<(Guid Id, string Code)> DebitNoteAsync(
        IAppDbContext db, Seed seed, DateOnly date, decimal quantity, decimal rate,
        Guid? sourcePurchaseBillId = null, Guid? productId = null, VatRate vatRate = VatRate.NoVat)
    {
        var created = await new CreateDebitNoteCommandHandler(db).Handle(
            new CreateDebitNoteCommand(
                seed.OrganizationId, seed.SupplierId, date, null, null,
                [new DebitNoteLineInput(productId ?? seed.ProductId, quantity, rate, vatRate)],
                sourcePurchaseBillId is null ? null : DocumentType.PurchaseBill,
                sourcePurchaseBillId),
            CancellationToken.None);

        var approved = await new ApproveDebitNoteCommandHandler(
                db, seed.NumberGenerator, new FakeCurrentUserService(Guid.NewGuid()), new DebitNotePostingRule(),
                new StockLedgerService(db))
            .Handle(new ApproveDebitNoteCommand(seed.OrganizationId, created.Id), CancellationToken.None);

        return (approved.Id, approved.Code);
    }

    private static async Task<CreateAccountResult> CreateAccountAsync(
        IAppDbContext db, FakeDocumentNumberGenerator numberGenerator, Guid organizationId, string name, Guid groupId) =>
        await new CreateAccountCommandHandler(db, numberGenerator).Handle(
            new CreateAccountCommand(organizationId, name, groupId), CancellationToken.None);
}
