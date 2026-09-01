using ErpApp.Application.Accounting.Commands.CreateAccount;
using ErpApp.Application.Accounting.Commands.CreateAccountGroup;
using ErpApp.Application.Catalog.Commands.CreateProduct;
using ErpApp.Application.Catalog.Commands.CreateProductCategory;
using ErpApp.Application.Catalog.Commands.CreateUnitOfMeasurement;
using ErpApp.Application.Common.Persistence;
using ErpApp.Application.Contacts.Commands.CreateContact;
using ErpApp.Application.Inventory.Stock;
using ErpApp.Application.Sales.Commands.ApproveInvoice;
using ErpApp.Application.Sales;
using ErpApp.Application.Sales.Commands.CreateInvoice;
using ErpApp.Application.Sales.Posting;
using ErpApp.Application.Sales.Queries.MigratedSalesRegister;
using ErpApp.Application.Sales.Queries.SalesRegister;
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

/// <summary>
/// Phase 21c -- the Migrated Sales Register. The interesting assertions here are the negative half
/// of the feature's promise: a migrated row is real enough for a statutory report and not real
/// enough to be anything else, so most of these tests prove where migrated rows do <b>not</b> turn
/// up.
/// </summary>
public class MigratedSalesRegisterQueryHandlerTests
{
    private static readonly DateOnly From = new(2024, 1, 1);
    private static readonly DateOnly To = new(2024, 12, 31);

    [Fact]
    public async Task Returns_migrated_rows_with_the_migrated_document_type_and_full_set_totals()
    {
        var db = TestAppDbContext.Create();
        var organizationId = Guid.NewGuid();

        AddEntry(db, organizationId, "INV-001", new DateOnly(2024, 3, 1), total: 113m, taxable: 100m, vat: 13m);
        AddEntry(db, organizationId, "INV-002", new DateOnly(2024, 3, 2), total: 226m, taxable: 200m, vat: 26m);
        // A sales return: a negative row, exactly as the live register renders a CreditNote.
        AddEntry(db, organizationId, "CN-001", new DateOnly(2024, 3, 3), total: -113m, taxable: -100m, vat: -13m);
        await db.SaveChangesAsync(CancellationToken.None);

        var result = await Handle(db, organizationId);

        Assert.Equal(3, result.Items.Count);
        Assert.All(result.Items, r => Assert.Equal(DocumentType.MigratedSalesEntry, r.DocumentType));
        Assert.Equal(226m, result.TotalValue);
        Assert.Equal(200m, result.TotalTaxableValue);
        Assert.Equal(26m, result.TotalVatAmount);
    }

    /// <summary>
    /// Both directions of the same claim, which is the one a future phase is most likely to break:
    /// the live register never shows a migrated row, and the migrated register never shows a live
    /// document. Same tenant, same date range, so nothing but the source separates them.
    /// </summary>
    [Fact]
    public async Task Migrated_rows_and_live_documents_never_appear_in_each_others_register()
    {
        var db = TestAppDbContext.Create();
        var seed = await SeedInvoiceTenantAsync(db);

        var invoice = await CreateAndApproveInvoiceAsync(db, seed, new DateOnly(2024, 3, 10), 100m);
        AddEntry(db, seed.OrganizationId, "OLD-9001", new DateOnly(2024, 3, 11), total: 113m, taxable: 100m, vat: 13m);
        await db.SaveChangesAsync(CancellationToken.None);

        var live = await new SalesRegisterQueryHandler(db).Handle(
            new SalesRegisterQuery(seed.OrganizationId, From, To, null, null), CancellationToken.None);

        var migrated = await Handle(db, seed.OrganizationId);
        var migratedRow = Assert.Single(migrated.Items).DocumentCode;

        Assert.Equal(invoice.Code, Assert.Single(live.Items).DocumentCode);
        Assert.Equal("OLD-9001", migratedRow);
    }

    /// <summary>There is no EF global query filter in this codebase, so this is asserted the strict
    /// way Phase 21b established: org B's rows must be <b>absent</b> from org A's answer, not merely
    /// outnumbered by it.</summary>
    [Fact]
    public async Task Another_organizations_migrated_rows_are_absent()
    {
        var db = TestAppDbContext.Create();
        var organizationA = Guid.NewGuid();
        var organizationB = Guid.NewGuid();

        AddEntry(db, organizationA, "A-001", new DateOnly(2024, 3, 1), total: 100m, taxable: 100m, vat: 0m);
        AddEntry(db, organizationB, "B-001", new DateOnly(2024, 3, 1), total: 999m, taxable: 999m, vat: 0m);
        await db.SaveChangesAsync(CancellationToken.None);

        var result = await Handle(db, organizationA);

        Assert.Equal("A-001", Assert.Single(result.Items).DocumentCode);
        Assert.DoesNotContain(result.Items, r => r.DocumentCode == "B-001");
        Assert.Equal(100m, result.TotalValue);
    }

    /// <summary>phase-16c bug #1: a footer total must cover the whole filtered set, never the page
    /// the client happens to hold. Proven with more rows than one page holds.</summary>
    [Fact]
    public async Task Totals_cover_the_full_filtered_set_not_the_current_page()
    {
        var db = TestAppDbContext.Create();
        var organizationId = Guid.NewGuid();

        for (var i = 1; i <= 12; i++)
        {
            AddEntry(db, organizationId, $"INV-{i:D3}", new DateOnly(2024, 3, i), total: 10m, taxable: 10m, vat: 0m);
        }

        await db.SaveChangesAsync(CancellationToken.None);

        var result = await Handle(db, organizationId, page: 2, pageSize: 5);

        Assert.Equal(5, result.Items.Count);
        Assert.Equal(12, result.TotalCount);
        Assert.Equal(120m, result.TotalValue);
    }

    [Fact]
    public async Task Party_search_matches_the_free_text_name_or_pan()
    {
        var db = TestAppDbContext.Create();
        var organizationId = Guid.NewGuid();

        AddEntry(db, organizationId, "INV-001", new DateOnly(2024, 3, 1), total: 10m, taxable: 10m, vat: 0m,
            partyName: "Himalayan Traders", partyPan: "301234567");
        AddEntry(db, organizationId, "INV-002", new DateOnly(2024, 3, 2), total: 20m, taxable: 20m, vat: 0m,
            partyName: "Everest Supplies", partyPan: "309999999");
        await db.SaveChangesAsync(CancellationToken.None);

        var byName = await Handle(db, organizationId, partySearch: "Himalayan");
        var byPan = await Handle(db, organizationId, partySearch: "309999");

        Assert.Equal("INV-001", Assert.Single(byName.Items).DocumentCode);
        Assert.Equal(10m, byName.TotalValue);
        Assert.Equal("INV-002", Assert.Single(byPan.Items).DocumentCode);
    }

    /// <summary>The four Export columns the live register can never populate (FR-5.8 is deferred to
    /// Phase 23) round-trip on a migrated row -- see the aggregate's doc comment for why this is the
    /// one place the migrated variant may legitimately show more than its live sibling.</summary>
    [Fact]
    public async Task Export_columns_round_trip_on_a_migrated_row()
    {
        var db = TestAppDbContext.Create();
        var organizationId = Guid.NewGuid();

        db.MigratedSalesRegisterEntries.Add(MigratedSalesRegisterEntry.Create(
            organizationId, new DateOnly(2024, 3, 1), "EXP-001", "Overseas Buyer LLC", null, null,
            totalValue: 500m, taxExemptValue: 0m, taxableValue: 500m, vatAmount: 0m,
            exportValue: 500m, exportCountry: "India", exportDeclarationNo: "DEC-77",
            exportDeclarationDate: new DateOnly(2024, 3, 2), now: DateTimeOffset.UtcNow));
        await db.SaveChangesAsync(CancellationToken.None);

        var row = Assert.Single((await Handle(db, organizationId)).Items);

        Assert.Equal(500m, row.ExportValue);
        Assert.Equal("India", row.ExportCountry);
        Assert.Equal("DEC-77", row.ExportDeclarationNo);
        Assert.Equal(new DateOnly(2024, 3, 2), row.ExportDeclarationDate);
    }

    private static Task<SalesRegisterDto> Handle(
        IAppDbContext db, Guid organizationId, string? partySearch = null, int page = 1, int pageSize = 50) =>
        new MigratedSalesRegisterQueryHandler(db).Handle(
            new MigratedSalesRegisterQuery(organizationId, From, To, partySearch, page, pageSize),
            CancellationToken.None);

    internal static void AddEntry(
        IAppDbContext db,
        Guid organizationId,
        string documentCode,
        DateOnly date,
        decimal total,
        decimal taxable,
        decimal vat,
        string partyName = "Himalayan Traders Private Limited",
        string? partyPan = "301234567") =>
        db.MigratedSalesRegisterEntries.Add(MigratedSalesRegisterEntry.Create(
            organizationId, date, documentCode, partyName, partyPan, null,
            total, taxExemptValue: 0m, taxableValue: taxable, vatAmount: vat,
            exportValue: 0m, exportCountry: null, exportDeclarationNo: null, exportDeclarationDate: null,
            now: DateTimeOffset.UtcNow));

    internal sealed record InvoiceSeed(
        Guid OrganizationId, FakeDocumentNumberGenerator NumberGenerator, Guid CustomerId, Guid WarehouseId, Guid ProductId);

    /// <summary>The smallest tenant an Invoice can actually be approved in -- customer, warehouse,
    /// a Service product (no stock to consume) and the three GL accounts the posting rule reads.</summary>
    internal static async Task<InvoiceSeed> SeedInvoiceTenantAsync(IAppDbContext db)
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

        return new InvoiceSeed(organizationId, numberGenerator, customer.Id, warehouse.Id, product.Id);
    }

    internal static async Task<(Guid Id, string Code)> CreateAndApproveInvoiceAsync(
        IAppDbContext db, InvoiceSeed seed, DateOnly date, decimal rate)
    {
        var created = await new CreateInvoiceCommandHandler(db).Handle(
            new CreateInvoiceCommand(
                seed.OrganizationId, seed.CustomerId, seed.WarehouseId, date, null,
                [new InvoiceLineInput(seed.ProductId, 1m, rate, VatRate.ThirteenPercentVat)]),
            CancellationToken.None);

        var stockLedgerService = new StockLedgerService(db);
        var approved = await new ApproveInvoiceCommandHandler(
                db, seed.NumberGenerator, new FakeCurrentUserService(Guid.NewGuid()), new InvoicePostingRule(),
                new FifoStockAvailabilityPolicy(db, stockLedgerService), stockLedgerService)
            .Handle(new ApproveInvoiceCommand(seed.OrganizationId, created.Id, OverrideWarning: false), CancellationToken.None);

        return (approved.Id, approved.Code);
    }
}
