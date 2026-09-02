using ErpApp.Application.Accounting;
using ErpApp.Application.Accounting.Commands.CreateAccount;
using ErpApp.Application.Accounting.Commands.CreateAccountGroup;
using ErpApp.Application.Accounting.Queries.VatSummaryReport;
using ErpApp.Application.Catalog.Commands.CreateProduct;
using ErpApp.Application.Catalog.Commands.CreateProductCategory;
using ErpApp.Application.Catalog.Commands.CreateUnitOfMeasurement;
using ErpApp.Application.Common.Persistence;
using ErpApp.Application.Contacts.Commands.CreateContact;
using ErpApp.Application.Inventory.Stock;
using ErpApp.Application.Sales;
using ErpApp.Application.Sales.Commands.ApproveInvoice;
using ErpApp.Application.Sales.Commands.CreateInvoice;
using ErpApp.Application.Sales.Commands.UpdateInvoice;
using ErpApp.Application.Sales.Posting;
using ErpApp.Application.Sales.Queries.GetInvoice;
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

/// <summary>
/// FR-5.8 (Phase 23): "An Invoice shall support marking itself as an export sale, <b>affecting its
/// tax treatment</b>." The phase's testing bar is explicit that the flag alone is not the thing to
/// assert -- the tax treatment is, in both directions: an export invoice must land in the Sales
/// Register's export columns <b>and must not</b> inflate its taxable-sales column or the VAT
/// Summary's taxable bucket; and an ordinary invoice must be completely unchanged, which is the
/// regression that actually matters.
///
/// The treatment itself was live-confirmed rather than inferred: on the reference product's Invoice
/// form, ticking "This is export sales" disables the per-line Tax selector and pins every line to
/// "0 Vat" (the control carries `ant-select-disabled` in the DOM). Zero-rated, not exempt.
/// </summary>
public class InvoiceExportSaleTests
{
    private static readonly DateOnly Jan10 = new(2026, 1, 10);
    private static readonly DateOnly Jan01 = new(2026, 1, 1);
    private static readonly DateOnly Jan31 = new(2026, 1, 31);

    [Fact]
    public async Task An_export_invoice_zero_rates_every_line_even_when_the_caller_asks_for_13_percent()
    {
        var db = TestAppDbContext.Create();
        var seed = await SeedAsync(db);

        // The caller explicitly asks for 13%. The live form does not even offer that choice once the
        // export box is ticked, so the aggregate enforces it rather than trusting the caller.
        var created = await CreateInvoiceAsync(db, seed, VatRate.ThirteenPercentVat, isExport: true);

        var invoice = await new GetInvoiceQueryHandler(db).Handle(
            new GetInvoiceQuery(seed.OrganizationId, created.Id), CancellationToken.None);

        Assert.True(invoice.IsExport);
        Assert.All(invoice.Lines, line => Assert.Equal(VatRate.ZeroVat, line.VatRate));
        Assert.All(invoice.Lines, line => Assert.Equal(0m, line.VatAmount));
        Assert.Equal(100m, invoice.GrandTotal);
    }

    [Fact]
    public async Task Zero_rated_is_not_the_same_bucket_as_exempt()
    {
        var db = TestAppDbContext.Create();
        var seed = await SeedAsync(db);

        var created = await CreateInvoiceAsync(db, seed, VatRate.ThirteenPercentVat, isExport: true);
        var invoice = await new GetInvoiceQueryHandler(db).Handle(
            new GetInvoiceQuery(seed.OrganizationId, created.Id), CancellationToken.None);

        // Both compute 0 VAT, so an implementation that reached for NoVat would pass every total
        // assertion in this file while filing the sale under the wrong statutory heading.
        Assert.All(invoice.Lines, line => Assert.NotEqual(VatRate.NoVat, line.VatRate));
    }

    [Fact]
    public async Task Ticking_export_on_an_existing_draft_re_rates_the_lines_already_on_it()
    {
        var db = TestAppDbContext.Create();
        var seed = await SeedAsync(db);

        // The other ordering: lines first at 13%, export ticked afterwards. Both orders have to end
        // in the same place or the invariant is not an invariant.
        var created = await CreateInvoiceAsync(db, seed, VatRate.ThirteenPercentVat, isExport: false);

        await new UpdateInvoiceCommandHandler(db).Handle(
            new UpdateInvoiceCommand(
                seed.OrganizationId, created.Id, seed.CustomerId, seed.WarehouseId, Jan10, null,
                [new InvoiceLineInput(seed.ProductId, 1m, 100m, VatRate.ThirteenPercentVat)],
                DiscountPct: 0,
                IsExport: true, ExportCountry: "India", ExportDeclarationNo: "EXP-001",
                ExportDeclarationDate: Jan10),
            CancellationToken.None);

        var invoice = await new GetInvoiceQueryHandler(db).Handle(
            new GetInvoiceQuery(seed.OrganizationId, created.Id), CancellationToken.None);

        Assert.True(invoice.IsExport);
        Assert.Equal("India", invoice.ExportCountry);
        Assert.Equal("EXP-001", invoice.ExportDeclarationNo);
        Assert.Equal(Jan10, invoice.ExportDeclarationDate);
        Assert.All(invoice.Lines, line => Assert.Equal(VatRate.ZeroVat, line.VatRate));
        Assert.Equal(0m, invoice.Lines.Sum(l => l.VatAmount));
    }

    [Fact]
    public async Task Clearing_the_export_flag_discards_the_export_details()
    {
        var db = TestAppDbContext.Create();
        var seed = await SeedAsync(db);

        var created = await CreateInvoiceAsync(db, seed, VatRate.ZeroVat, isExport: true);

        await new UpdateInvoiceCommandHandler(db).Handle(
            new UpdateInvoiceCommand(
                seed.OrganizationId, created.Id, seed.CustomerId, seed.WarehouseId, Jan10, null,
                [new InvoiceLineInput(seed.ProductId, 1m, 100m, VatRate.ThirteenPercentVat)],
                DiscountPct: 0,
                IsExport: false, ExportCountry: "India", ExportDeclarationNo: "EXP-001",
                ExportDeclarationDate: Jan10),
            CancellationToken.None);

        var invoice = await new GetInvoiceQueryHandler(db).Handle(
            new GetInvoiceQuery(seed.OrganizationId, created.Id), CancellationToken.None);

        // Same shape as PurchaseBill's import block: detail fields are meaningless without the flag,
        // so they are dropped rather than left behind to confuse a later reader or report.
        Assert.False(invoice.IsExport);
        Assert.Null(invoice.ExportCountry);
        Assert.Null(invoice.ExportDeclarationNo);
        Assert.Null(invoice.ExportDeclarationDate);
        // ...and with the flag off, the line keeps the rate the caller asked for.
        Assert.All(invoice.Lines, line => Assert.Equal(VatRate.ThirteenPercentVat, line.VatRate));
    }

    [Fact]
    public async Task The_export_details_stay_optional_when_the_flag_is_set()
    {
        var db = TestAppDbContext.Create();
        var seed = await SeedAsync(db);

        // Live-confirmed: the reference product marks Customer/Invoice Date/Due Date/Warehouse with a
        // required asterisk and pointedly does not mark Country, Date or Document No. This is the
        // one place FR-5.8 deliberately diverges from PurchaseBill's import block, whose equivalents
        // ARE required when flagged.
        var created = await CreateInvoiceAsync(db, seed, VatRate.ZeroVat, isExport: true, country: null);

        var invoice = await new GetInvoiceQueryHandler(db).Handle(
            new GetInvoiceQuery(seed.OrganizationId, created.Id), CancellationToken.None);

        Assert.True(invoice.IsExport);
        Assert.Null(invoice.ExportCountry);
    }

    [Fact]
    public async Task An_export_invoice_fills_the_registers_export_columns_without_inflating_taxable_sales()
    {
        var db = TestAppDbContext.Create();
        var seed = await SeedAsync(db);

        var exportInvoice = await CreateAndApproveAsync(db, seed, VatRate.ThirteenPercentVat, isExport: true);

        var result = await new SalesRegisterQueryHandler(db).Handle(
            new SalesRegisterQuery(seed.OrganizationId, Jan01, Jan31, null, null), CancellationToken.None);

        var row = Assert.Single(result.Items, r => r.DocumentCode == exportInvoice.Code);

        // Half one: it appears in the export columns, which were hardcoded to zero/null from Phase 19
        // until this phase.
        Assert.Equal(100m, row.ExportValue);
        Assert.Equal("India", row.ExportCountry);
        Assert.Equal("EXP-001", row.ExportDeclarationNo);
        Assert.Equal(Jan10, row.ExportDeclarationDate);

        // Half two, and the one that matters: it must NOT be counted as a taxable sale. Phase 6's
        // bug #3 is the standing reminder that a report change can look balanced and still put a
        // number in the wrong column.
        Assert.Equal(0m, row.TaxableValue);
        Assert.Equal(0m, row.VatAmount);
        Assert.Equal(100m, row.TaxExemptValue);
        Assert.Equal(100m, row.TotalValue);

        Assert.Equal(0m, result.TotalTaxableValue);
        Assert.Equal(0m, result.TotalVatAmount);
    }

    [Fact]
    public async Task An_ordinary_invoice_is_completely_unchanged_by_this_phase()
    {
        var db = TestAppDbContext.Create();
        var seed = await SeedAsync(db);

        var ordinary = await CreateAndApproveAsync(db, seed, VatRate.ThirteenPercentVat, isExport: false);

        var result = await new SalesRegisterQueryHandler(db).Handle(
            new SalesRegisterQuery(seed.OrganizationId, Jan01, Jan31, null, null), CancellationToken.None);

        var row = Assert.Single(result.Items, r => r.DocumentCode == ordinary.Code);

        Assert.Equal(100m, row.TaxableValue);
        Assert.Equal(13m, row.VatAmount);
        Assert.Equal(113m, row.TotalValue);
        Assert.Equal(0m, row.TaxExemptValue);

        // The four export columns stay empty for a non-export sale -- exactly as before Phase 23.
        Assert.Equal(0m, row.ExportValue);
        Assert.Null(row.ExportCountry);
        Assert.Null(row.ExportDeclarationNo);
        Assert.Null(row.ExportDeclarationDate);
    }

    [Fact]
    public async Task An_export_sale_lands_in_the_VAT_summarys_zero_rated_bucket_not_its_taxable_one()
    {
        var db = TestAppDbContext.Create();
        var seed = await SeedAsync(db);

        await CreateAndApproveAsync(db, seed, VatRate.ThirteenPercentVat, isExport: true);

        var report = await new VatSummaryReportQueryHandler(db).Handle(
            new VatSummaryReportQuery(seed.OrganizationId, Jan01, Jan31), CancellationToken.None);

        var thirteen = Assert.Single(report.SalesBuckets, b => b.VatRate == VatRate.ThirteenPercentVat);
        var zeroRated = Assert.Single(report.SalesBuckets, b => b.VatRate == VatRate.ZeroVat);
        var exempt = Assert.Single(report.SalesBuckets, b => b.VatRate == VatRate.NoVat);

        Assert.Equal(0m, thirteen.NetSalesAmount);
        Assert.Equal(0m, thirteen.OutputVatAmount);
        Assert.Equal(100m, zeroRated.NetSalesAmount);
        Assert.Equal(0m, zeroRated.OutputVatAmount);
        // Zero-rated and exempt are different statutory buckets; the sale must be in exactly one.
        Assert.Equal(0m, exempt.NetSalesAmount);
        Assert.Equal(0m, report.TotalOutputVat);
    }

    // --- helpers --------------------------------------------------------------

    private static async Task<CreateInvoiceResult> CreateInvoiceAsync(
        IAppDbContext db, Seed seed, VatRate vatRate, bool isExport, string? country = "India")
    {
        return await new CreateInvoiceCommandHandler(db).Handle(
            new CreateInvoiceCommand(
                seed.OrganizationId, seed.CustomerId, seed.WarehouseId, Jan10, null,
                [new InvoiceLineInput(seed.ProductId, 1m, 100m, vatRate)],
                ReferrerType: null, ReferrerId: null, DiscountPct: 0,
                IsExport: isExport,
                ExportCountry: isExport ? country : null,
                ExportDeclarationNo: isExport ? "EXP-001" : null,
                ExportDeclarationDate: isExport ? Jan10 : null),
            CancellationToken.None);
    }

    private static async Task<(Guid Id, string Code)> CreateAndApproveAsync(
        IAppDbContext db, Seed seed, VatRate vatRate, bool isExport)
    {
        var created = await CreateInvoiceAsync(db, seed, vatRate, isExport);
        var stockLedgerService = new StockLedgerService(db);
        var approved = await new ApproveInvoiceCommandHandler(
            db, seed.NumberGenerator, new FakeCurrentUserService(Guid.NewGuid()), new InvoicePostingRule(),
            new FifoStockAvailabilityPolicy(db, stockLedgerService), stockLedgerService)
            .Handle(new ApproveInvoiceCommand(seed.OrganizationId, created.Id, OverrideWarning: false), CancellationToken.None);
        return (approved.Id, approved.Code);
    }

    private sealed record Seed(
        Guid OrganizationId, FakeDocumentNumberGenerator NumberGenerator, Guid CustomerId, Guid WarehouseId, Guid ProductId);

    /// <summary>Same seed as SalesRegisterQueryHandlerTests, so an export row is compared against a
    /// baseline this suite already trusts rather than a differently-built one.</summary>
    private static async Task<Seed> SeedAsync(IAppDbContext db)
    {
        var organizationId = Guid.NewGuid();
        var numberGenerator = new FakeDocumentNumberGenerator();

        var customer = await new CreateContactCommandHandler(db, numberGenerator).Handle(
            new CreateContactCommand(organizationId, ContactType.Customer, "Acme Exports", null, null, null, null, null, 0m),
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
}
