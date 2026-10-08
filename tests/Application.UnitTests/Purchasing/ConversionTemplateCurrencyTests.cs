using ErpApp.Application.Accounting.Commands.CreateAccount;
using ErpApp.Application.Accounting.Commands.CreateAccountGroup;
using ErpApp.Application.Catalog.Commands.CreateProduct;
using ErpApp.Application.Catalog.Commands.CreateProductCategory;
using ErpApp.Application.Catalog.Commands.CreateUnitOfMeasurement;
using ErpApp.Application.Common.Exceptions;
using ErpApp.Application.Common.Persistence;
using ErpApp.Application.Contacts.Commands.CreateContact;
using ErpApp.Application.Inventory.Stock;
using ErpApp.Application.Purchasing;
using ErpApp.Application.Purchasing.Commands.ApprovePurchaseBill;
using ErpApp.Application.Purchasing.Commands.CreateDebitNote;
using ErpApp.Application.Purchasing.Commands.CreatePurchaseBill;
using ErpApp.Application.Purchasing.Commands.UpdateDebitNote;
using ErpApp.Application.Purchasing.Posting;
using ErpApp.Application.Purchasing.Queries.GetDebitNoteConversionTemplate;
using ErpApp.Application.Purchasing.Queries.GetPurchaseBillConversionTemplate;
using ErpApp.Application.Sales.Queries.GetInvoiceConversionTemplate;
using ErpApp.Application.Tenancy.Commands.CreateWarehouse;
using ErpApp.Application.UnitTests.TestSupport;
using ErpApp.Domain.Accounting;
using ErpApp.Domain.Catalog;
using ErpApp.Domain.Common;
using ErpApp.Domain.Contacts;
using ErpApp.Domain.Purchasing;
using ErpApp.Domain.Sales;
using ErpApp.Domain.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.UnitTests.Purchasing;

/// <summary>
/// Phase 69 -- every conversion template carries its source's currency and rate. Phase 35a's comments
/// said they already did; none of these four did, so a USD source converted to a base-currency document
/// at rate 1. The two reversals (Credit Note, Debit Note) now also refuse another currency, and an edit
/// of a converted draft is held to what is left of each line, as Create always was. The credit-note
/// half is in Sales/CreditNoteInvoiceReferenceTests.
/// </summary>
public class ConversionTemplateCurrencyTests
{
    private const decimal UsdRate = 133m;
    private static readonly DateOnly Day = new(2026, 9, 1);

    [Fact]
    public async Task A_quotation_and_a_purchase_order_hand_their_currency_to_the_document_they_become()
    {
        var (db, seed) = await SeedAsync();

        var quotation = Quotation.Create(seed.OrganizationId, seed.CustomerId, Day, null, null);
        quotation.SetCurrency("USD", UsdRate);
        quotation.AddLine(seed.ProductId, 1, 10m, VatRate.ThirteenPercentVat, 0, null, 1m);
        quotation.Approve(Guid.NewGuid(), "QUO0001");
        var order = PurchaseOrder.Create(seed.OrganizationId, seed.SupplierId, Day, null);
        order.SetCurrency("USD", UsdRate);
        order.AddLine(seed.ProductId, 1, 10m, VatRate.ThirteenPercentVat, 0, null, 1m);
        order.Approve(Guid.NewGuid(), "PO0001");
        db.Quotations.Add(quotation);
        db.PurchaseOrders.Add(order);
        await db.SaveChangesAsync();

        var invoiceTemplate = await new GetInvoiceConversionTemplateQueryHandler(db).Handle(
            new GetInvoiceConversionTemplateQuery(seed.OrganizationId, quotation.Id), CancellationToken.None);
        var billTemplate = await new GetPurchaseBillConversionTemplateQueryHandler(db).Handle(
            new GetPurchaseBillConversionTemplateQuery(seed.OrganizationId, order.Id), CancellationToken.None);

        Assert.Equal(("USD", UsdRate), (invoiceTemplate.CurrencyCode, invoiceTemplate.ExchangeRate));
        Assert.Equal(("USD", UsdRate), (billTemplate.CurrencyCode, billTemplate.ExchangeRate));
    }

    [Fact]
    public async Task A_debit_note_converted_from_a_foreign_bill_keeps_its_currency_and_refuses_another()
    {
        var (db, seed) = await SeedAsync();
        var billId = await ApprovedForeignBillAsync(db, seed);

        var template = await new GetDebitNoteConversionTemplateQueryHandler(db).Handle(
            new GetDebitNoteConversionTemplateQuery(seed.OrganizationId, billId), CancellationToken.None);
        Assert.Equal(("USD", UsdRate), (template.CurrencyCode, template.ExchangeRate));

        var refused = await Assert.ThrowsAsync<ConflictException>(() => new CreateDebitNoteCommandHandler(db).Handle(
            new CreateDebitNoteCommand(
                seed.OrganizationId, seed.SupplierId, Day, null, null, template.Lines,
                DocumentType.PurchaseBill, billId),
            CancellationToken.None));
        Assert.Contains("USD", refused.Message);

        await new CreateDebitNoteCommandHandler(db).Handle(
            new CreateDebitNoteCommand(
                seed.OrganizationId, seed.SupplierId, Day, null, null, template.Lines,
                DocumentType.PurchaseBill, billId)
            {
                CurrencyCode = template.CurrencyCode,
                ExchangeRate = template.ExchangeRate,
            },
            CancellationToken.None);
    }

    [Fact]
    public async Task Editing_a_converted_debit_note_is_held_to_what_is_left_of_each_line()
    {
        var (db, seed) = await SeedAsync();
        var billId = await ApprovedForeignBillAsync(db, seed);
        var created = await new CreateDebitNoteCommandHandler(db).Handle(
            new CreateDebitNoteCommand(
                seed.OrganizationId, seed.SupplierId, Day, null, null,
                [new DebitNoteLineInput(seed.ProductId, 1m, 10m, VatRate.ThirteenPercentVat)],
                DocumentType.PurchaseBill, billId)
            {
                CurrencyCode = "USD",
                ExchangeRate = UsdRate,
            },
            CancellationToken.None);

        UpdateDebitNoteCommand Edit(decimal quantity) => new(
            seed.OrganizationId, created.Id, seed.SupplierId, Day, null, null,
            [new DebitNoteLineInput(seed.ProductId, quantity, 10m, VatRate.ThirteenPercentVat)])
        {
            CurrencyCode = "USD",
            ExchangeRate = UsdRate,
        };

        var refused = await Assert.ThrowsAsync<ConflictException>(
            () => new UpdateDebitNoteCommandHandler(db).Handle(Edit(3m), CancellationToken.None));
        Assert.Contains("only 2 remains", refused.Message);

        // Its own saved quantity is not counted against it.
        await new UpdateDebitNoteCommandHandler(db).Handle(Edit(2m), CancellationToken.None);
    }

    // ---------------------------------------------------------------------------------------------

    private sealed record Seed(
        Guid OrganizationId, FakeDocumentNumberGenerator NumberGenerator, Guid CustomerId, Guid SupplierId,
        Guid WarehouseId, Guid ProductId);

    private static async Task<Guid> ApprovedForeignBillAsync(IAppDbContext db, Seed seed)
    {
        var created = await new CreatePurchaseBillCommandHandler(db).Handle(
            new CreatePurchaseBillCommand(
                seed.OrganizationId, seed.SupplierId, seed.WarehouseId, Day, null, null, false, null, null, null, null,
                [new PurchaseBillLineInput(seed.ProductId, 2m, 10m, VatRate.ThirteenPercentVat, ExpenditureClassification.Others)])
            {
                CurrencyCode = "USD",
                ExchangeRate = UsdRate,
            },
            CancellationToken.None);

        await new ApprovePurchaseBillCommandHandler(
            db, seed.NumberGenerator, new FakeCurrentUserService(Guid.NewGuid()), new PurchaseBillPostingRule(),
            new StockLedgerService(db))
            .Handle(new ApprovePurchaseBillCommand(seed.OrganizationId, created.Id), CancellationToken.None);

        return created.Id;
    }

    private static async Task<(IAppDbContext Db, Seed Seed)> SeedAsync()
    {
        var db = TestAppDbContext.Create();
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

        var settings = TenantSettings.CreateDefault(organizationId);
        settings.SetAccountingDefaults(sales.Id, ar.Id, vatPayable.Id, purchase.Id, ap.Id, vatReceivable.Id, null);
        db.TenantSettings.Add(settings);
        await db.SaveChangesAsync(CancellationToken.None);

        return (db, new Seed(organizationId, numberGenerator, customer.Id, supplier.Id, warehouse.Id, product.Id));
    }
}
