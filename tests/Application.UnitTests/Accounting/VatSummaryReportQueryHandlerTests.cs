using ErpApp.Application.Accounting;
using ErpApp.Application.Accounting.Commands.CreateAccount;
using ErpApp.Application.Accounting.Commands.CreateAccountGroup;
using ErpApp.Application.Accounting.Posting;
using ErpApp.Application.Accounting.Queries.VatSummaryReport;
using ErpApp.Application.Catalog.Commands.CreateProduct;
using ErpApp.Application.Catalog.Commands.CreateProductCategory;
using ErpApp.Application.Catalog.Commands.CreateUnitOfMeasurement;
using ErpApp.Application.Common.Exceptions;
using ErpApp.Application.Common.Persistence;
using ErpApp.Application.Contacts.Commands.CreateContact;
using ErpApp.Application.Inventory.Stock;
using ErpApp.Application.Purchasing;
using ErpApp.Application.Purchasing.Commands.ApproveDebitNote;
using ErpApp.Application.Purchasing.Commands.ApproveExpense;
using ErpApp.Application.Purchasing.Commands.ApprovePurchaseBill;
using ErpApp.Application.Purchasing.Commands.CreateDebitNote;
using ErpApp.Application.Purchasing.Commands.CreateExpense;
using ErpApp.Application.Purchasing.Commands.CreatePurchaseBill;
using ErpApp.Application.Purchasing.Posting;
using ErpApp.Application.Purchasing.Queries.PurchaseRegister;
using ErpApp.Application.Sales;
using ErpApp.Application.Sales.Commands.ApproveCreditNote;
using ErpApp.Application.Sales.Commands.ApproveInvoice;
using ErpApp.Application.Sales.Commands.CreateCreditNote;
using ErpApp.Application.Sales.Commands.CreateInvoice;
using ErpApp.Application.Sales.Credit;
using ErpApp.Application.Sales.Posting;
using ErpApp.Application.Sales.Queries.SalesRegister;
using ErpApp.Application.Sales.Stock;
using ErpApp.Application.Tenancy.Commands.CreateWarehouse;
using ErpApp.Application.UnitTests.TestSupport;
using ErpApp.Domain.Accounting;
using ErpApp.Domain.Catalog;
using ErpApp.Domain.Common;
using ErpApp.Domain.Contacts;
using ErpApp.Domain.Purchasing;
using ErpApp.Domain.Tenancy;

namespace ErpApp.Application.UnitTests.Accounting;

public class VatSummaryReportQueryHandlerTests
{
    /// <summary>Fiscal year 2082-83: Shrawan 2082 (from 2025-07-17) to Asar 2083. Its columns are 0 = Shrawan … 11 = Asar.</summary>
    private const int Fy = 2082;

    private static DateOnly Bs(int year, int month, int day) => BsCalendar.ToGregorian(new BsDate(year, month, day))!.Value;

    private static VatReturnLineDto Line(VatSummaryReportDto report, string key) => Assert.Single(report.Lines, x => x.Key == key);

    private static Task<VatSummaryReportDto> ReturnAsync(IAppDbContext db, Seed seed, int fiscalYear = Fy) =>
        new VatSummaryReportQueryHandler(db).Handle(new VatSummaryReportQuery(seed.OrganizationId, fiscalYear), CancellationToken.None);

    [Fact]
    public async Task The_return_is_twelve_BS_months_of_a_fiscal_year_in_the_forms_order()
    {
        var db = TestAppDbContext.Create();
        var seed = await SeedAsync(db);

        var report = await ReturnAsync(db, seed);

        Assert.Equal(Fy, report.FiscalYear);
        Assert.Equal(12, report.Columns.Count);
        Assert.Equal("Shrawan 2082", report.Columns[0].Label);
        Assert.Equal("Asar 2083", report.Columns[11].Label);
        Assert.Equal(Bs(2082, 4, 1), report.FromDate);
        Assert.Equal(report.Columns[11].ToDate, report.ToDate);
        Assert.Equal(VatReturnLines.All.Select(x => x.Key), report.Lines.Select(x => x.Key));
        Assert.All(report.Lines, x => Assert.Equal(12, x.Monthly.Count));
        Assert.All(report.Lines, x => Assert.False(string.IsNullOrWhiteSpace(x.LabelNepali)));
    }

    [Fact]
    public async Task A_months_documents_land_in_its_column_net_of_returns_and_drafts_and_other_years_stay_out()
    {
        var db = TestAppDbContext.Create();
        var seed = await SeedAsync(db);

        // Aswin 2082 (column 2): a taxable sale and an exempt one, a credit note, a bill and a debit note.
        await CreateAndApproveInvoiceAsync(db, seed, Bs(2082, 6, 5), 10m, 100m, VatRate.ThirteenPercentVat);
        await CreateAndApproveInvoiceAsync(db, seed, Bs(2082, 6, 6), 5m, 50m, VatRate.NoVat);
        await CreateAndApproveStandaloneCreditNoteAsync(db, seed, Bs(2082, 6, 8), 2m, 100m, VatRate.ThirteenPercentVat);
        await CreateAndApprovePurchaseBillAsync(db, seed, Bs(2082, 6, 10), 4m, 100m, VatRate.ThirteenPercentVat);
        await CreateAndApproveStandaloneDebitNoteAsync(db, seed, Bs(2082, 6, 12), 1m, 100m, VatRate.ThirteenPercentVat);
        // A draft, and an invoice in the next fiscal year: neither counts.
        await CreateInvoiceAsync(db, seed, Bs(2082, 6, 20), 5m, 50m, VatRate.ThirteenPercentVat);
        await CreateAndApproveInvoiceAsync(db, seed, Bs(2083, 4, 2), 2m, 20m, VatRate.ThirteenPercentVat);

        var report = await ReturnAsync(db, seed);
        const int aswin = 2;

        Assert.Equal(1_000m, Line(report, VatReturnLines.TaxableSales).Monthly[aswin]);
        Assert.Equal(130m, Line(report, VatReturnLines.TaxableSalesVat).Monthly[aswin]);
        Assert.Equal(250m, Line(report, VatReturnLines.LocalExemptSales).Monthly[aswin]);
        Assert.Equal(0m, Line(report, VatReturnLines.ExportSales).Monthly[aswin]);
        Assert.Equal(1_250m, Line(report, VatReturnLines.TotalSales).Monthly[aswin]);
        Assert.Equal(200m, Line(report, VatReturnLines.TaxableSalesReturns).Monthly[aswin]);
        Assert.Equal(26m, Line(report, VatReturnLines.TaxableSalesReturnsVat).Monthly[aswin]);
        Assert.Equal(200m, Line(report, VatReturnLines.TotalSalesReturns).Monthly[aswin]);
        Assert.Equal(400m, Line(report, VatReturnLines.TaxablePurchases).Monthly[aswin]);
        Assert.Equal(52m, Line(report, VatReturnLines.TaxablePurchasesVat).Monthly[aswin]);
        Assert.Equal(400m, Line(report, VatReturnLines.TotalPurchases).Monthly[aswin]);
        Assert.Equal(100m, Line(report, VatReturnLines.TaxablePurchaseReturns).Monthly[aswin]);
        Assert.Equal(13m, Line(report, VatReturnLines.TaxablePurchaseReturnsVat).Monthly[aswin]);

        // (130 - 26) - (52 - 13) = 65 payable, with nothing brought forward.
        Assert.Equal(65m, Line(report, VatReturnLines.MonthVat).Monthly[aswin]);
        Assert.Equal(0m, Line(report, VatReturnLines.CreditBroughtForward).Monthly[aswin]);
        Assert.Equal(65m, Line(report, VatReturnLines.Payable).Monthly[aswin]);

        // The other eleven months are empty, and the year's totals are Aswin's.
        Assert.Equal(1_000m, Line(report, VatReturnLines.TaxableSales).Total);
        Assert.Equal(65m, report.NetVat);
        Assert.Null(Line(report, VatReturnLines.CreditBroughtForward).Total);
        Assert.Null(Line(report, VatReturnLines.Payable).Total);
    }

    [Fact]
    public async Task A_months_credit_is_brought_forward_and_used_up_by_the_next_payable_month()
    {
        var db = TestAppDbContext.Create();
        var seed = await SeedAsync(db);

        // Shrawan: output 13, input 130 -> a credit of 117.
        await CreateAndApproveInvoiceAsync(db, seed, Bs(2082, 4, 5), 1m, 100m, VatRate.ThirteenPercentVat);
        await CreateAndApprovePurchaseBillAsync(db, seed, Bs(2082, 4, 6), 10m, 100m, VatRate.ThirteenPercentVat);
        // Bhadra: output 260 -> 260 less the 117 brought forward = 143 payable.
        await CreateAndApproveInvoiceAsync(db, seed, Bs(2082, 5, 5), 20m, 100m, VatRate.ThirteenPercentVat);

        var report = await ReturnAsync(db, seed);

        Assert.Equal(-117m, Line(report, VatReturnLines.MonthVat).Monthly[0]);
        Assert.Equal(-117m, Line(report, VatReturnLines.Payable).Monthly[0]);
        Assert.Equal(117m, Line(report, VatReturnLines.CreditBroughtForward).Monthly[1]);
        Assert.Equal(260m, Line(report, VatReturnLines.MonthVat).Monthly[1]);
        Assert.Equal(143m, Line(report, VatReturnLines.Payable).Monthly[1]);
        // Paid in Bhadra, so nothing carries into Aswin.
        Assert.Equal(0m, Line(report, VatReturnLines.CreditBroughtForward).Monthly[2]);
        Assert.Equal(0m, Line(report, VatReturnLines.Payable).Monthly[2]);
    }

    /// <summary>The user's ruling (2026-10-12): an unused credit carries across the fiscal year's boundary, so Shrawan
    /// opens with what every earlier month left. A payable month in between pays from the credit; it never adds to it.</summary>
    [Fact]
    public async Task Shrawan_brings_forward_the_credit_every_earlier_month_left()
    {
        var db = TestAppDbContext.Create();
        var seed = await SeedAsync(db);

        // Fiscal year 2081-82: Magh a credit of 117, Falgun 13 payable against it (104 left), Asar a further 117.
        await CreateAndApprovePurchaseBillAsync(db, seed, Bs(2081, 10, 5), 10m, 100m, VatRate.ThirteenPercentVat);
        await CreateAndApproveInvoiceAsync(db, seed, Bs(2081, 10, 6), 1m, 100m, VatRate.ThirteenPercentVat);
        await CreateAndApproveInvoiceAsync(db, seed, Bs(2081, 11, 6), 1m, 100m, VatRate.ThirteenPercentVat);
        await CreateAndApprovePurchaseBillAsync(db, seed, Bs(2082, 3, 5), 10m, 100m, VatRate.ThirteenPercentVat);
        await CreateAndApproveInvoiceAsync(db, seed, Bs(2082, 3, 6), 1m, 100m, VatRate.ThirteenPercentVat);

        var report = await ReturnAsync(db, seed);

        Assert.Equal(221m, Line(report, VatReturnLines.CreditBroughtForward).Monthly[0]); // 117 - 13 + 117
        Assert.Equal(-221m, Line(report, VatReturnLines.Payable).Monthly[0]);
        Assert.Equal(0m, Line(report, VatReturnLines.TaxableSales).Total);
    }

    [Fact]
    public async Task An_expense_is_a_purchase_its_VAT_claimed_and_its_untaxed_value_exempt()
    {
        var db = TestAppDbContext.Create();
        var seed = await SeedAsync(db);

        await CreateAndApproveExpenseAsync(db, seed, Bs(2082, 6, 5), 200m, VatRate.ThirteenPercentVat);
        await CreateAndApproveExpenseAsync(db, seed, Bs(2082, 6, 6), 50m, VatRate.NoVat);

        var report = await ReturnAsync(db, seed);

        Assert.Equal(200m, Line(report, VatReturnLines.TaxablePurchases).Monthly[2]);
        Assert.Equal(26m, Line(report, VatReturnLines.TaxablePurchasesVat).Monthly[2]);
        Assert.Equal(50m, Line(report, VatReturnLines.ExemptPurchases).Monthly[2]);
        Assert.Equal(250m, Line(report, VatReturnLines.TotalPurchases).Monthly[2]);
        Assert.Equal(-26m, Line(report, VatReturnLines.MonthVat).Monthly[2]);
    }

    /// <summary>
    /// Phase 89 -- <b>a month of the return is the registers over that month</b>, by construction, because all three read
    /// the same four readers (phase 36: two reports agree only through one reader plus a test reading both). Every kind of
    /// figure is seeded into one month: a taxable, an exempt, an export and a foreign sale, a return, a local, an import
    /// and a capital purchase, a debit note and an expense, and each line of the return is checked against the register
    /// column it comes from. The Sales Register nets its credit notes and the Purchase Register its debit notes, so the
    /// comparison is net of returns.
    /// </summary>
    [Fact]
    public async Task A_months_return_equals_the_sales_and_purchase_registers_over_that_month()
    {
        var db = TestAppDbContext.Create();
        var seed = await SeedAsync(db);
        var day = Bs(2082, 6, 1);

        await CreateAndApproveInvoiceAsync(db, seed, day.AddDays(1), 3m, 100m, VatRate.ThirteenPercentVat);
        await CreateAndApproveInvoiceAsync(db, seed, day.AddDays(2), 2m, 75m, VatRate.NoVat);
        await CreateAndApproveExportInvoiceAsync(db, seed, day.AddDays(3), 4m, 90m);
        await CreateAndApproveForeignInvoiceAsync(db, seed, day.AddDays(4), 1m, 10m, VatRate.ThirteenPercentVat, "USD", 133.25m);
        await CreateAndApproveStandaloneCreditNoteAsync(db, seed, day.AddDays(5), 1m, 100m, VatRate.ThirteenPercentVat);
        await CreateAndApprovePurchaseBillAsync(db, seed, day.AddDays(6), 5m, 40m, VatRate.ThirteenPercentVat);
        await CreateAndApprovePurchaseBillAsync(db, seed, day.AddDays(7), 2m, 300m, VatRate.ThirteenPercentVat, isImport: true);
        await CreateAndApprovePurchaseBillAsync(
            db, seed, day.AddDays(8), 1m, 900m, VatRate.ThirteenPercentVat, classification: ExpenditureClassification.Capital);
        await CreateAndApproveStandaloneDebitNoteAsync(db, seed, day.AddDays(9), 1m, 40m, VatRate.ThirteenPercentVat);
        await CreateAndApproveExpenseAsync(db, seed, day.AddDays(10), 120m, VatRate.ThirteenPercentVat);
        await CreateAndApproveExpenseAsync(db, seed, day.AddDays(11), 30m, VatRate.NoVat);

        var report = await ReturnAsync(db, seed);
        var month = report.Columns[2];
        decimal M(string key) => Line(report, key).Monthly[2];

        var sales = await new SalesRegisterQueryHandler(db, new FakeCurrentUserService(Guid.NewGuid())).Handle(
            new SalesRegisterQuery(seed.OrganizationId, month.FromDate, month.ToDate, null, null, ExportAll: true),
            CancellationToken.None);
        var purchases = await new PurchaseRegisterQueryHandler(db, new FakeCurrentUserService(Guid.NewGuid())).Handle(
            new PurchaseRegisterQuery(seed.OrganizationId, month.FromDate, month.ToDate, null, ExportAll: true),
            CancellationToken.None);

        Assert.Equal(sales.TotalTaxableValue, M(VatReturnLines.TaxableSales) - M(VatReturnLines.TaxableSalesReturns));
        Assert.Equal(sales.TotalVatAmount, M(VatReturnLines.TaxableSalesVat) - M(VatReturnLines.TaxableSalesReturnsVat));
        Assert.Equal(
            sales.TotalTaxExemptValue,
            M(VatReturnLines.LocalExemptSales) + M(VatReturnLines.ExportSales) - M(VatReturnLines.LocalExemptSalesReturns));
        Assert.Equal(sales.Items.Sum(x => x.ExportValue), M(VatReturnLines.ExportSales));

        Assert.Equal(purchases.TotalTaxableNonCapitalLocalValue, M(VatReturnLines.TaxablePurchases) - M(VatReturnLines.TaxablePurchaseReturns));
        Assert.Equal(purchases.TotalTaxableNonCapitalLocalVat, M(VatReturnLines.TaxablePurchasesVat) - M(VatReturnLines.TaxablePurchaseReturnsVat));
        Assert.Equal(purchases.TotalTaxableNonCapitalImportValue, M(VatReturnLines.TaxableImports) - M(VatReturnLines.TaxableImportReturns));
        Assert.Equal(purchases.TotalTaxableNonCapitalImportVat, M(VatReturnLines.TaxableImportsVat) - M(VatReturnLines.TaxableImportReturnsVat));
        Assert.Equal(purchases.TotalTaxableCapitalValue, M(VatReturnLines.CapitalPurchases) - M(VatReturnLines.CapitalReturns));
        Assert.Equal(purchases.TotalTaxableCapitalVat, M(VatReturnLines.CapitalPurchasesVat) - M(VatReturnLines.CapitalReturnsVat));
        Assert.Equal(purchases.TotalTaxExemptValue, M(VatReturnLines.ExemptPurchases) - M(VatReturnLines.ExemptPurchaseReturns));

        // Every term is non-zero, so no identity above passes by being 0 = 0 (phase 66's fixture rule).
        Assert.All(
            new[]
            {
                VatReturnLines.TaxableSales, VatReturnLines.LocalExemptSales, VatReturnLines.ExportSales,
                VatReturnLines.TaxableSalesReturns, VatReturnLines.TaxablePurchases, VatReturnLines.TaxableImports,
                VatReturnLines.CapitalPurchases, VatReturnLines.ExemptPurchases, VatReturnLines.TaxablePurchaseReturns,
            },
            key => Assert.NotEqual(0m, M(key)));
    }

    /// <summary>Phase 89 -- the user's ruling (2026-10-12): an expense is a purchase, so the Purchase Register lists it,
    /// typed Expense, in the column its VAT decides, and the return claims the same figure.</summary>
    [Fact]
    public async Task The_purchase_register_lists_an_approved_expense_and_not_a_draft_one()
    {
        var db = TestAppDbContext.Create();
        var seed = await SeedAsync(db);
        var day = Bs(2082, 6, 1);

        var expense = await CreateAndApproveExpenseAsync(db, seed, day.AddDays(1), 120m, VatRate.ThirteenPercentVat);
        await new CreateExpenseCommandHandler(db).Handle(
            new CreateExpenseCommand(
                seed.OrganizationId, seed.SupplierId, day.AddDays(2), null, null, null, false, null,
                [new ExpenseLineInput(seed.PurchaseAccountId, 999m, VatRate.ThirteenPercentVat)]),
            CancellationToken.None);

        var register = await new PurchaseRegisterQueryHandler(db, new FakeCurrentUserService(Guid.NewGuid())).Handle(
            new PurchaseRegisterQuery(seed.OrganizationId, day, day.AddDays(20), null), CancellationToken.None);

        var row = Assert.Single(register.Items);
        Assert.Equal(DocumentType.Expense, row.DocumentType);
        Assert.Equal(expense.Code, row.DocumentCode);
        Assert.Equal(120m, row.TaxableNonCapitalLocalValue);
        Assert.Equal(15.6m, row.TaxableNonCapitalLocalVat);
        Assert.Equal("Global Supplies", row.ContactName);
    }

    [Fact]
    public async Task A_fiscal_year_outside_the_BS_table_is_not_found()
    {
        var db = TestAppDbContext.Create();
        var seed = await SeedAsync(db);

        await Assert.ThrowsAsync<NotFoundException>(() => ReturnAsync(db, seed, fiscalYear: 2095));
    }

    // See SalesMasterReportQueryHandlerTests' matching comment: a single FakeDocumentNumberGenerator
    // instance is shared across every Create/Approve call in one test, or every approved document
    // collides on the same "Foo-0001" code.
    private sealed record Seed(
        Guid OrganizationId, FakeDocumentNumberGenerator NumberGenerator, Guid CustomerId, Guid SupplierId,
        Guid WarehouseId, Guid ProductId, Guid PurchaseAccountId);

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

        return new Seed(organizationId, numberGenerator, customer.Id, supplier.Id, warehouse.Id, product.Id, purchase.Id);
    }

    /// <summary>
    /// Phase 44 (43 Decision D) -- <b>the VAT return is filed in NPR, so this report reports NPR.</b> All four document
    /// types carry a foreign document in one month, which is the case that matters: a month nets invoice lines against
    /// credit note lines, so a fold after the netting would subtract figures still in two currencies. Phase 89: the fold
    /// now happens in the shared readers, and this holds them to it.
    /// </summary>
    [Fact]
    public async Task Foreign_currency_documents_are_reported_in_base_currency()
    {
        var db = TestAppDbContext.Create();
        var seed = await SeedAsync(db);
        var month = Bs(2082, 6, 1);

        // 100 USD at 133 = 13,300, VAT 13 USD = 1,729.
        await CreateAndApproveForeignInvoiceAsync(db, seed, month.AddDays(1), 1m, 100m, VatRate.ThirteenPercentVat, "USD", 133m);
        // 40 USD at 133 = 5,320, VAT 5.2 USD = 691.60.
        await CreateAndApproveForeignCreditNoteAsync(db, seed, month.AddDays(2), 1m, 40m, VatRate.ThirteenPercentVat, "USD", 133m);
        // 200 USD at 133 = 26,600, VAT 26 USD = 3,458.
        await CreateAndApproveForeignPurchaseBillAsync(db, seed, month.AddDays(3), 1m, 200m, VatRate.ThirteenPercentVat, "USD", 133m);
        // 50 USD at 133 = 6,650, VAT 6.5 USD = 864.50.
        await CreateAndApproveForeignDebitNoteAsync(db, seed, month.AddDays(4), 1m, 50m, VatRate.ThirteenPercentVat, "USD", 133m);

        var report = await ReturnAsync(db, seed);

        Assert.Equal(13_300m, Line(report, VatReturnLines.TaxableSales).Monthly[2]);
        Assert.Equal(1_729m, Line(report, VatReturnLines.TaxableSalesVat).Monthly[2]);
        Assert.Equal(5_320m, Line(report, VatReturnLines.TaxableSalesReturns).Monthly[2]);
        Assert.Equal(691.60m, Line(report, VatReturnLines.TaxableSalesReturnsVat).Monthly[2]);
        Assert.Equal(26_600m, Line(report, VatReturnLines.TaxablePurchases).Monthly[2]);
        Assert.Equal(3_458m, Line(report, VatReturnLines.TaxablePurchasesVat).Monthly[2]);
        Assert.Equal(6_650m, Line(report, VatReturnLines.TaxablePurchaseReturns).Monthly[2]);
        Assert.Equal(864.50m, Line(report, VatReturnLines.TaxablePurchaseReturnsVat).Monthly[2]);
        // (1,729 - 691.60) - (3,458 - 864.50) = 1,037.40 - 2,593.50
        Assert.Equal(-1_556.10m, Line(report, VatReturnLines.MonthVat).Monthly[2]);
    }

    private static async Task CreateAndApproveForeignInvoiceAsync(
        IAppDbContext db, Seed seed, DateOnly date, decimal quantity, decimal rate, VatRate vatRate,
        string currencyCode, decimal exchangeRate)
    {
        var created = await new CreateInvoiceCommandHandler(db, await SalesPricing.PricerAsync(db, seed.OrganizationId)).Handle(
            new CreateInvoiceCommand(
                seed.OrganizationId, seed.CustomerId, seed.WarehouseId, date, null,
                [new InvoiceLineInput(seed.ProductId, quantity, rate, vatRate)])
            {
                CurrencyCode = currencyCode,
                ExchangeRate = exchangeRate,
            },
            CancellationToken.None);

        var stockLedgerService = new StockLedgerService(db);
        await new ApproveInvoiceCommandHandler(
            db, seed.NumberGenerator, new FakeCurrentUserService(Guid.NewGuid()), new InvoicePostingRule(),
            new FifoStockAvailabilityPolicy(db, stockLedgerService), stockLedgerService, new ContactCreditLimitPolicy(db))
            .Handle(new ApproveInvoiceCommand(seed.OrganizationId, created.Id, OverrideWarning: false), CancellationToken.None);
    }

    private static async Task CreateAndApproveForeignCreditNoteAsync(
        IAppDbContext db, Seed seed, DateOnly date, decimal quantity, decimal rate, VatRate vatRate,
        string currencyCode, decimal exchangeRate)
    {
        var created = await new CreateCreditNoteCommandHandler(db).Handle(
            new CreateCreditNoteCommand(
                seed.OrganizationId, seed.CustomerId, date, null,
                [new CreditNoteLineInput(seed.ProductId, quantity, rate, vatRate)])
            {
                CurrencyCode = currencyCode,
                ExchangeRate = exchangeRate,
            },
            CancellationToken.None);

        await new ApproveCreditNoteCommandHandler(
            db, seed.NumberGenerator, new FakeCurrentUserService(Guid.NewGuid()),
            new CreditNotePostingRule(), new StockLedgerService(db))
            .Handle(new ApproveCreditNoteCommand(seed.OrganizationId, created.Id), CancellationToken.None);
    }

    private static async Task CreateAndApproveForeignPurchaseBillAsync(
        IAppDbContext db, Seed seed, DateOnly date, decimal quantity, decimal rate, VatRate vatRate,
        string currencyCode, decimal exchangeRate)
    {
        var created = await new CreatePurchaseBillCommandHandler(db).Handle(
            new CreatePurchaseBillCommand(
                seed.OrganizationId, seed.SupplierId, seed.WarehouseId, date, null, null, false, null, null, null, null,
                [new PurchaseBillLineInput(seed.ProductId, quantity, rate, vatRate, ExpenditureClassification.Others)])
            {
                CurrencyCode = currencyCode,
                ExchangeRate = exchangeRate,
            },
            CancellationToken.None);

        await new ApprovePurchaseBillCommandHandler(
            db, seed.NumberGenerator, new FakeCurrentUserService(Guid.NewGuid()), new PurchaseBillPostingRule(),
            new StockLedgerService(db))
            .Handle(new ApprovePurchaseBillCommand(seed.OrganizationId, created.Id), CancellationToken.None);
    }

    private static async Task CreateAndApproveForeignDebitNoteAsync(
        IAppDbContext db, Seed seed, DateOnly date, decimal quantity, decimal rate, VatRate vatRate,
        string currencyCode, decimal exchangeRate)
    {
        var created = await new CreateDebitNoteCommandHandler(db).Handle(
            new CreateDebitNoteCommand(
                seed.OrganizationId, seed.SupplierId, date, null, null,
                [new DebitNoteLineInput(seed.ProductId, quantity, rate, vatRate)])
            {
                CurrencyCode = currencyCode,
                ExchangeRate = exchangeRate,
            },
            CancellationToken.None);

        await new ApproveDebitNoteCommandHandler(
            db, seed.NumberGenerator, new FakeCurrentUserService(Guid.NewGuid()),
            new DebitNotePostingRule(), new StockLedgerService(db))
            .Handle(new ApproveDebitNoteCommand(seed.OrganizationId, created.Id), CancellationToken.None);
    }

    private static async Task<CreateInvoiceResult> CreateInvoiceAsync(
        IAppDbContext db, Seed seed, DateOnly date, decimal quantity, decimal rate, VatRate vatRate)
    {
        return await new CreateInvoiceCommandHandler(db, await SalesPricing.PricerAsync(db, seed.OrganizationId)).Handle(
            new CreateInvoiceCommand(
                seed.OrganizationId, seed.CustomerId, seed.WarehouseId, date, null,
                [new InvoiceLineInput(seed.ProductId, quantity, rate, vatRate)]),
            CancellationToken.None);
    }

    private static async Task CreateAndApproveInvoiceAsync(
        IAppDbContext db, Seed seed, DateOnly date, decimal quantity, decimal rate, VatRate vatRate)
    {
        var created = await CreateInvoiceAsync(db, seed, date, quantity, rate, vatRate);
        var stockLedgerService = new StockLedgerService(db);
        await new ApproveInvoiceCommandHandler(
            db, seed.NumberGenerator, new FakeCurrentUserService(Guid.NewGuid()), new InvoicePostingRule(),
            new FifoStockAvailabilityPolicy(db, stockLedgerService), stockLedgerService, new ContactCreditLimitPolicy(db))
            .Handle(new ApproveInvoiceCommand(seed.OrganizationId, created.Id, OverrideWarning: false), CancellationToken.None);
    }

    private static async Task CreateAndApproveStandaloneCreditNoteAsync(
        IAppDbContext db, Seed seed, DateOnly date, decimal quantity, decimal rate, VatRate vatRate)
    {
        var created = await new CreateCreditNoteCommandHandler(db).Handle(
            new CreateCreditNoteCommand(
                seed.OrganizationId, seed.CustomerId, date, null,
                [new CreditNoteLineInput(seed.ProductId, quantity, rate, vatRate)]),
            CancellationToken.None);

        await new ApproveCreditNoteCommandHandler(
            db, seed.NumberGenerator, new FakeCurrentUserService(Guid.NewGuid()),
            new CreditNotePostingRule(), new StockLedgerService(db))
            .Handle(new ApproveCreditNoteCommand(seed.OrganizationId, created.Id), CancellationToken.None);
    }

    private static async Task CreateAndApprovePurchaseBillAsync(
        IAppDbContext db, Seed seed, DateOnly date, decimal quantity, decimal rate, VatRate vatRate,
        bool isImport = false, ExpenditureClassification classification = ExpenditureClassification.Others)
    {
        var created = await new CreatePurchaseBillCommandHandler(db).Handle(
            new CreatePurchaseBillCommand(
                seed.OrganizationId, seed.SupplierId, seed.WarehouseId, date, null, null, isImport,
                isImport ? "India" : null, isImport ? date : null, isImport ? "IMP-001" : null, null,
                [new PurchaseBillLineInput(seed.ProductId, quantity, rate, vatRate, classification)]),
            CancellationToken.None);

        await new ApprovePurchaseBillCommandHandler(
            db, seed.NumberGenerator, new FakeCurrentUserService(Guid.NewGuid()), new PurchaseBillPostingRule(),
            new StockLedgerService(db))
            .Handle(new ApprovePurchaseBillCommand(seed.OrganizationId, created.Id), CancellationToken.None);
    }

    private static async Task CreateAndApproveStandaloneDebitNoteAsync(
        IAppDbContext db, Seed seed, DateOnly date, decimal quantity, decimal rate, VatRate vatRate)
    {
        var created = await new CreateDebitNoteCommandHandler(db).Handle(
            new CreateDebitNoteCommand(
                seed.OrganizationId, seed.SupplierId, date, null, null,
                [new DebitNoteLineInput(seed.ProductId, quantity, rate, vatRate)]),
            CancellationToken.None);

        await new ApproveDebitNoteCommandHandler(
            db, seed.NumberGenerator, new FakeCurrentUserService(Guid.NewGuid()),
            new DebitNotePostingRule(), new StockLedgerService(db))
            .Handle(new ApproveDebitNoteCommand(seed.OrganizationId, created.Id), CancellationToken.None);
    }

    private static async Task<(Guid Id, string Code)> CreateAndApproveExpenseAsync(
        IAppDbContext db, Seed seed, DateOnly date, decimal amount, VatRate vatRate)
    {
        var created = await new CreateExpenseCommandHandler(db).Handle(
            new CreateExpenseCommand(
                seed.OrganizationId, seed.SupplierId, date, null, null, null, false, null,
                [new ExpenseLineInput(seed.PurchaseAccountId, amount, vatRate)]),
            CancellationToken.None);

        var approved = await new ApproveExpenseCommandHandler(
            db, seed.NumberGenerator, new FakeCurrentUserService(Guid.NewGuid()), new ExpensePostingRule())
            .Handle(new ApproveExpenseCommand(seed.OrganizationId, created.Id), CancellationToken.None);
        return (approved.Id, approved.Code);
    }

    private static async Task CreateAndApproveExportInvoiceAsync(
        IAppDbContext db, Seed seed, DateOnly date, decimal quantity, decimal rate)
    {
        var created = await new CreateInvoiceCommandHandler(db, await SalesPricing.PricerAsync(db, seed.OrganizationId)).Handle(
            new CreateInvoiceCommand(
                seed.OrganizationId, seed.CustomerId, seed.WarehouseId, date, null,
                [new InvoiceLineInput(seed.ProductId, quantity, rate, VatRate.ThirteenPercentVat)],
                ReferrerType: null, ReferrerId: null, DiscountPct: 0,
                IsExport: true, ExportCountry: "India", ExportDeclarationNo: "EXP-001", ExportDeclarationDate: date),
            CancellationToken.None);

        var stockLedgerService = new StockLedgerService(db);
        await new ApproveInvoiceCommandHandler(
            db, seed.NumberGenerator, new FakeCurrentUserService(Guid.NewGuid()), new InvoicePostingRule(),
            new FifoStockAvailabilityPolicy(db, stockLedgerService), stockLedgerService, new ContactCreditLimitPolicy(db))
            .Handle(new ApproveInvoiceCommand(seed.OrganizationId, created.Id, OverrideWarning: false), CancellationToken.None);
    }
}
