using ErpApp.Application.Accounting;
using ErpApp.Application.Accounting.Commands.CreateAccount;
using ErpApp.Application.Accounting.Commands.CreateAccountGroup;
using ErpApp.Application.Catalog.Commands.CreateProduct;
using ErpApp.Application.Catalog.Commands.CreateProductCategory;
using ErpApp.Application.Catalog.Commands.CreateUnitOfMeasurement;
using ErpApp.Application.Common.Persistence;
using ErpApp.Application.Configuration.Commands.CreateReportingTagCategory;
using ErpApp.Application.Configuration.Commands.CreateReportingTagOption;
using ErpApp.Application.Configuration.Commands.SetTransactionReportingTags;
using ErpApp.Application.Contacts.Commands.CreateContact;
using ErpApp.Application.Inventory.Stock;
using ErpApp.Application.Sales;
using ErpApp.Application.Sales.Commands.ApproveCreditNote;
using ErpApp.Application.Sales.Commands.ApproveInvoice;
using ErpApp.Application.Sales.Commands.CreateCreditNote;
using ErpApp.Application.Sales.Commands.CreateInvoice;
using ErpApp.Application.Sales.Posting;
using ErpApp.Application.Sales.Queries.SalesRegister;
using ErpApp.Application.Sales.Stock;
using ErpApp.Application.Tenancy.Commands.CreateWarehouse;
using ErpApp.Application.UnitTests.TestSupport;
using ErpApp.Domain.Accounting;
using ErpApp.Domain.Catalog;
using ErpApp.Domain.Common;
using ErpApp.Domain.Contacts;
using ErpApp.Domain.Tenancy;

namespace ErpApp.Application.UnitTests.Sales;

public class SalesRegisterQueryHandlerTests
{
    [Fact]
    public async Task Handle_combines_invoices_and_credit_notes_splitting_tax_exempt_from_taxable_value()
    {
        var db = TestAppDbContext.Create();
        var seed = await SeedAsync(db);

        var taxableInvoice = await CreateAndApproveInvoiceAsync(db, seed, new DateOnly(2026, 1, 10), 100m, VatRate.ThirteenPercentVat);
        var exemptInvoice = await CreateAndApproveInvoiceAsync(db, seed, new DateOnly(2026, 1, 12), 50m, VatRate.NoVat);
        var creditNote = await CreateAndApproveStandaloneCreditNoteAsync(db, seed, new DateOnly(2026, 1, 15), 20m, VatRate.ThirteenPercentVat);
        await CreateInvoiceAsync(db, seed, new DateOnly(2026, 1, 20), 999m, VatRate.NoVat); // Draft -- excluded

        var handler = new SalesRegisterQueryHandler(db);
        var result = await handler.Handle(
            new SalesRegisterQuery(seed.OrganizationId, new DateOnly(2026, 1, 1), new DateOnly(2026, 1, 31), null, null),
            CancellationToken.None);

        Assert.Equal(3, result.Items.Count);

        var taxableRow = Assert.Single(result.Items, r => r.DocumentCode == taxableInvoice.Code);
        Assert.Equal(DocumentType.Invoice, taxableRow.DocumentType);
        Assert.Equal(100m, taxableRow.TaxableValue);
        Assert.Equal(0m, taxableRow.TaxExemptValue);
        Assert.Equal(13m, taxableRow.VatAmount);
        Assert.Equal(113m, taxableRow.TotalValue);

        var exemptRow = Assert.Single(result.Items, r => r.DocumentCode == exemptInvoice.Code);
        Assert.Equal(50m, exemptRow.TaxExemptValue);
        Assert.Equal(0m, exemptRow.TaxableValue);
        Assert.Equal(0m, exemptRow.VatAmount);

        var creditNoteRow = Assert.Single(result.Items, r => r.DocumentCode == creditNote.Code);
        Assert.Equal(DocumentType.CreditNote, creditNoteRow.DocumentType);
        Assert.Equal(-20m, creditNoteRow.TaxableValue);
        Assert.Equal(-2.6m, creditNoteRow.VatAmount);

        Assert.Equal(result.Items.Sum(r => r.TotalValue), result.TotalValue);
        Assert.Equal(result.Items.Sum(r => r.TaxableValue), result.TotalTaxableValue);
        Assert.Equal(result.Items.Sum(r => r.TaxExemptValue), result.TotalTaxExemptValue);
        Assert.Equal(result.Items.Sum(r => r.VatAmount), result.TotalVatAmount);
    }

    /// <summary>Phase 19 decision #1/exit criterion #2 -- tagging one Invoice with Tag A narrows the
    /// register to just that row and excludes both the untagged Invoice and every CreditNote (which
    /// can never carry a tag, since Reporting Tags only attach to Quotation/Invoice).</summary>
    [Fact]
    public async Task Handle_narrows_to_tagged_invoices_and_excludes_every_credit_note_when_a_tag_filter_is_active()
    {
        var db = TestAppDbContext.Create();
        var seed = await SeedAsync(db);

        var taggedInvoice = await CreateAndApproveInvoiceAsync(db, seed, new DateOnly(2026, 1, 10), 100m, VatRate.ThirteenPercentVat);
        var untaggedInvoice = await CreateAndApproveInvoiceAsync(db, seed, new DateOnly(2026, 1, 11), 200m, VatRate.ThirteenPercentVat);
        await CreateAndApproveStandaloneCreditNoteAsync(db, seed, new DateOnly(2026, 1, 12), 10m, VatRate.ThirteenPercentVat);

        var category = await new CreateReportingTagCategoryCommandHandler(db).Handle(
            new CreateReportingTagCategoryCommand(seed.OrganizationId, "Project"), CancellationToken.None);
        var tagOption = await new CreateReportingTagOptionCommandHandler(db).Handle(
            new CreateReportingTagOptionCommand(seed.OrganizationId, "Project A", category.Id), CancellationToken.None);
        await new SetTransactionReportingTagsCommandHandler(db).Handle(
            new SetTransactionReportingTagsCommand(seed.OrganizationId, DocumentType.Invoice, taggedInvoice.Id, [tagOption.Id]),
            CancellationToken.None);

        var handler = new SalesRegisterQueryHandler(db);
        var unfiltered = await handler.Handle(
            new SalesRegisterQuery(seed.OrganizationId, new DateOnly(2026, 1, 1), new DateOnly(2026, 1, 31), null, null),
            CancellationToken.None);
        Assert.Equal(3, unfiltered.Items.Count);

        var filtered = await handler.Handle(
            new SalesRegisterQuery(seed.OrganizationId, new DateOnly(2026, 1, 1), new DateOnly(2026, 1, 31), null, [tagOption.Id]),
            CancellationToken.None);

        var row = Assert.Single(filtered.Items);
        Assert.Equal(taggedInvoice.Code, row.DocumentCode);
        Assert.Equal(113m, filtered.TotalValue);
        Assert.DoesNotContain(filtered.Items, r => r.DocumentCode == untaggedInvoice.Code);
    }

    private sealed record Seed(
        Guid OrganizationId, FakeDocumentNumberGenerator NumberGenerator, Guid CustomerId, Guid WarehouseId, Guid ProductId);

    private static async Task<Seed> SeedAsync(IAppDbContext db)
    {
        var organizationId = Guid.NewGuid();
        var numberGenerator = new FakeDocumentNumberGenerator();

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

        return new Seed(organizationId, numberGenerator, customer.Id, warehouse.Id, product.Id);
    }

    private static async Task<CreateInvoiceResult> CreateInvoiceAsync(
        IAppDbContext db, Seed seed, DateOnly date, decimal rate, VatRate vatRate)
    {
        return await new CreateInvoiceCommandHandler(db).Handle(
            new CreateInvoiceCommand(
                seed.OrganizationId, seed.CustomerId, seed.WarehouseId, date, null,
                [new InvoiceLineInput(seed.ProductId, 1m, rate, vatRate)]),
            CancellationToken.None);
    }

    private static async Task<(Guid Id, string Code)> CreateAndApproveInvoiceAsync(
        IAppDbContext db, Seed seed, DateOnly date, decimal rate, VatRate vatRate)
    {
        var created = await CreateInvoiceAsync(db, seed, date, rate, vatRate);
        var stockLedgerService = new StockLedgerService(db);
        var approved = await new ApproveInvoiceCommandHandler(
            db, seed.NumberGenerator, new FakeCurrentUserService(Guid.NewGuid()), new InvoicePostingRule(),
            new FifoStockAvailabilityPolicy(db, stockLedgerService), stockLedgerService)
            .Handle(new ApproveInvoiceCommand(seed.OrganizationId, created.Id, OverrideWarning: false), CancellationToken.None);
        return (approved.Id, approved.Code);
    }

    private static async Task<(Guid Id, string Code)> CreateAndApproveStandaloneCreditNoteAsync(
        IAppDbContext db, Seed seed, DateOnly date, decimal rate, VatRate vatRate)
    {
        var created = await new CreateCreditNoteCommandHandler(db).Handle(
            new CreateCreditNoteCommand(
                seed.OrganizationId, seed.CustomerId, date, null,
                [new CreditNoteLineInput(seed.ProductId, 1m, rate, vatRate)]),
            CancellationToken.None);

        var approved = await new ApproveCreditNoteCommandHandler(
            db, seed.NumberGenerator, new FakeCurrentUserService(Guid.NewGuid()),
            new CreditNotePostingRule(), new StockLedgerService(db))
            .Handle(new ApproveCreditNoteCommand(seed.OrganizationId, created.Id), CancellationToken.None);

        return (approved.Id, approved.Code);
    }
}
