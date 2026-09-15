using ErpApp.Application.Accounting;
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
using ErpApp.Application.Purchasing.Queries.PurchaseRegister;
using ErpApp.Application.Purchasing.Queries.PurchaseReturnRegister;
using ErpApp.Application.Tenancy.Commands.CreateWarehouse;
using ErpApp.Application.UnitTests.TestSupport;
using ErpApp.Domain.Accounting;
using ErpApp.Domain.Catalog;
using ErpApp.Domain.Common;
using ErpApp.Domain.Contacts;
using ErpApp.Domain.Purchasing;
using ErpApp.Domain.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.UnitTests.Purchasing;

public class PurchaseRegisterQueryHandlerTests
{
    /// <summary>Phase 19 decision #3 -- the 4-bucket split (Tax-Exempt / Taxable Non-Capital Local /
    /// Taxable Non-Capital Import / Taxable Capital) reuses PurchaseBill's existing IsImport
    /// (Phase 6) and PurchaseBillLine's ExpenditureClassification (Phase 8e) -- no domain gap.</summary>
    [Fact]
    public async Task Handle_buckets_purchase_bill_lines_by_tax_exempt_capital_and_import_status()
    {
        var db = TestAppDbContext.Create();
        var seed = await SeedAsync(db);

        var exemptBill = await CreateAndApprovePurchaseBillAsync(
            db, seed, new DateOnly(2026, 1, 10), isImport: false, importDocumentNo: null,
            [(seed.ProductId, 10m, 50m, VatRate.NoVat, ExpenditureClassification.Others)]);

        var localTaxableBill = await CreateAndApprovePurchaseBillAsync(
            db, seed, new DateOnly(2026, 1, 11), isImport: false, importDocumentNo: null,
            [(seed.ProductId, 5m, 100m, VatRate.ThirteenPercentVat, ExpenditureClassification.Others)]);

        var importTaxableBill = await CreateAndApprovePurchaseBillAsync(
            db, seed, new DateOnly(2026, 1, 12), isImport: true, importDocumentNo: "IMP-001",
            [(seed.ProductId, 2m, 200m, VatRate.ThirteenPercentVat, ExpenditureClassification.Others)]);

        var capitalBill = await CreateAndApprovePurchaseBillAsync(
            db, seed, new DateOnly(2026, 1, 13), isImport: false, importDocumentNo: null,
            [(seed.ProductId, 1m, 1000m, VatRate.ThirteenPercentVat, ExpenditureClassification.Capital)]);

        var handler = new PurchaseRegisterQueryHandler(db, new FakeCurrentUserService(Guid.NewGuid()));
        var result = await handler.Handle(
            new PurchaseRegisterQuery(seed.OrganizationId, new DateOnly(2026, 1, 1), new DateOnly(2026, 1, 31), null),
            CancellationToken.None);

        Assert.Equal(4, result.Items.Count);

        var exemptRow = Assert.Single(result.Items, r => r.DocumentCode == exemptBill.Code);
        Assert.Equal(500m, exemptRow.TaxExemptValue); // 10 * 50

        var localRow = Assert.Single(result.Items, r => r.DocumentCode == localTaxableBill.Code);
        Assert.Equal(500m, localRow.TaxableNonCapitalLocalValue); // 5 * 100
        Assert.Equal(65m, localRow.TaxableNonCapitalLocalVat);
        Assert.Equal(0m, localRow.TaxableNonCapitalImportValue);

        var importRow = Assert.Single(result.Items, r => r.DocumentCode == importTaxableBill.Code);
        Assert.Equal("IMP-001", importRow.ImportDeclarationNo);
        Assert.Equal(400m, importRow.TaxableNonCapitalImportValue); // 2 * 200
        Assert.Equal(52m, importRow.TaxableNonCapitalImportVat);
        Assert.Equal(0m, importRow.TaxableNonCapitalLocalValue);

        var capitalRow = Assert.Single(result.Items, r => r.DocumentCode == capitalBill.Code);
        Assert.Equal(1000m, capitalRow.TaxableCapitalValue);
        Assert.Equal(130m, capitalRow.TaxableCapitalVat);

        Assert.Equal(result.Items.Sum(r => r.TaxExemptValue), result.TotalTaxExemptValue);
        Assert.Equal(result.Items.Sum(r => r.TaxableCapitalValue), result.TotalTaxableCapitalValue);
    }

    /// <summary>A DebitNote reversing a PurchaseBill carries no ExpenditureClassification/IsImport
    /// of its own -- both are resolved from the source line via the same (ProductId, Rate, VatRate)
    /// key AnnexThirteenReportQueryHandler already uses, and appear as negative values.</summary>
    [Fact]
    public async Task Handle_resolves_a_linked_debit_notes_classification_from_its_source_purchase_bill()
    {
        var db = TestAppDbContext.Create();
        var seed = await SeedAsync(db);

        var bill = await CreateAndApprovePurchaseBillAsync(
            db, seed, new DateOnly(2026, 1, 10), isImport: false, importDocumentNo: null,
            [(seed.ProductId, 10m, 100m, VatRate.ThirteenPercentVat, ExpenditureClassification.Capital)]);

        var debitNote = await CreateAndApproveDebitNoteAsync(
            db, seed, new DateOnly(2026, 1, 15), 2m, 100m, bill.Id);

        var handler = new PurchaseRegisterQueryHandler(db, new FakeCurrentUserService(Guid.NewGuid()));
        var result = await handler.Handle(
            new PurchaseRegisterQuery(seed.OrganizationId, new DateOnly(2026, 1, 1), new DateOnly(2026, 1, 31), null),
            CancellationToken.None);

        var debitNoteRow = Assert.Single(result.Items, r => r.DocumentCode == debitNote.Code);
        Assert.Equal(DocumentType.DebitNote, debitNoteRow.DocumentType);
        Assert.Equal(-200m, debitNoteRow.TaxableCapitalValue); // -(2 * 100), resolved as Capital from the source line
        Assert.Equal(-26m, debitNoteRow.TaxableCapitalVat);
    }

    private sealed record Seed(
        Guid OrganizationId, FakeDocumentNumberGenerator NumberGenerator, Guid SupplierId, Guid WarehouseId, Guid ProductId);

    private static async Task<Seed> SeedAsync(IAppDbContext db)
    {
        var organizationId = Guid.NewGuid();
        var numberGenerator = new FakeDocumentNumberGenerator();

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
                organizationId, ProductType.Goods, "Widget", category.Id, unit.Id, null, true, 150m, 100m,
                VatRate.NoVat, 0, true),
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
        var inventory = await new CreateAccountCommandHandler(db, numberGenerator).Handle(
            new CreateAccountCommand(organizationId, "Inventory", assetGroup.Id), CancellationToken.None);
        var cogs = await new CreateAccountCommandHandler(db, numberGenerator).Handle(
            new CreateAccountCommand(organizationId, "Cost of Goods Sold", expenseGroup.Id), CancellationToken.None);

        var settings = TenantSettings.CreateDefault(organizationId);
        settings.SetAccountingDefaults(null, null, null, purchase.Id, ap.Id, vatReceivable.Id, null);
        settings.SetInventoryDefaults(inventory.Id, cogs.Id, null, null);
        db.TenantSettings.Add(settings);
        await db.SaveChangesAsync(CancellationToken.None);

        return new Seed(organizationId, numberGenerator, supplier.Id, warehouse.Id, product.Id);
    }

    /// <summary>
    /// Phase 44 (43 Decision D) -- <b>the Purchase Register is filed in NPR, so it reports NPR.</b>
    /// The sales-side counterpart of this test shipped in phase 43; this is the same assertion on
    /// the purchase side, where the register carries seven statutory magnitudes rather than four.
    ///
    /// <para>A 100 USD bill at 133 contributes 13,300 -- not 100 -- and its 13% VAT contributes
    /// 1,729. The partition is asserted as well as the magnitudes: the seven buckets must still add
    /// up to the same total, which is the property folding per line (rather than per bucket)
    /// preserves and folding the finished buckets would break.</para>
    /// </summary>
    [Fact]
    public async Task A_foreign_currency_bill_is_reported_in_base_currency_and_still_partitions()
    {
        var db = TestAppDbContext.Create();
        var seed = await SeedAsync(db);

        await CreateAndApproveForeignPurchaseBillAsync(
            db, seed, new DateOnly(2026, 1, 10), 100m, "USD", 133m);

        var result = await new PurchaseRegisterQueryHandler(db, new FakeCurrentUserService(Guid.NewGuid()))
            .Handle(
                new PurchaseRegisterQuery(seed.OrganizationId, new DateOnly(2026, 1, 1), new DateOnly(2026, 1, 31), null),
                CancellationToken.None);

        var row = Assert.Single(result.Items);
        Assert.Equal(13_300m, row.TaxableNonCapitalLocalValue);
        Assert.Equal(1_729m, row.TaxableNonCapitalLocalVat);

        // Nothing leaked into another bucket on the way through the fold.
        Assert.Equal(0m, row.TaxExemptValue);
        Assert.Equal(0m, row.TaxableNonCapitalImportValue);
        Assert.Equal(0m, row.TaxableCapitalValue);

        // The partition: the row's buckets sum to the register's footer, in rupees.
        var rowSum =
            row.TaxExemptValue
            + row.TaxableNonCapitalLocalValue + row.TaxableNonCapitalLocalVat
            + row.TaxableNonCapitalImportValue + row.TaxableNonCapitalImportVat
            + row.TaxableCapitalValue + row.TaxableCapitalVat;
        var footerSum =
            result.TotalTaxExemptValue
            + result.TotalTaxableNonCapitalLocalValue + result.TotalTaxableNonCapitalLocalVat
            + result.TotalTaxableNonCapitalImportValue + result.TotalTaxableNonCapitalImportVat
            + result.TotalTaxableCapitalValue + result.TotalTaxableCapitalVat;
        Assert.Equal(15_029m, rowSum);
        Assert.Equal(rowSum, footerSum);
    }

    /// <summary>
    /// Phase 44 -- the return half folds too, and it folds <b>inside PurchaseReturnReader</b>, which
    /// the Purchase Return Register also reads. Asserting both reports on the same note is the point:
    /// phase 26c's whole reason for the shared reader was that two registers agree by construction,
    /// and folding in one caller would have made this register report a foreign return in rupees and
    /// the other report the same return in dollars.
    /// </summary>
    [Fact]
    public async Task A_foreign_currency_return_folds_once_in_the_reader_both_registers_read()
    {
        var db = TestAppDbContext.Create();
        var seed = await SeedAsync(db);

        var bill = await CreateAndApproveForeignPurchaseBillAsync(
            db, seed, new DateOnly(2026, 1, 10), 100m, "USD", 133m);
        // Quantity 0.4 at the bill's own rate, not a different rate: the conversion cap keys on
        // (ProductId, Rate, VatRate, Discount), so a return quotes its source line's rate (phase 6 #4).
        await CreateAndApproveForeignDebitNoteAsync(
            db, seed, new DateOnly(2026, 1, 12), 0.4m, 100m, "USD", 133m, bill.Id);

        var register = await new PurchaseRegisterQueryHandler(db, new FakeCurrentUserService(Guid.NewGuid()))
            .Handle(
                new PurchaseRegisterQuery(seed.OrganizationId, new DateOnly(2026, 1, 1), new DateOnly(2026, 1, 31), null),
                CancellationToken.None);
        var returnRegister = await new PurchaseReturnRegisterQueryHandler(db, new FakeCurrentUserService(Guid.NewGuid()))
            .Handle(
                new PurchaseReturnRegisterQuery(seed.OrganizationId, new DateOnly(2026, 1, 1), new DateOnly(2026, 1, 31), null),
                CancellationToken.None);

        // 40 USD at 133 = 5,320, plus 13% VAT of 5.2 USD = 691.60.
        var noteRow = Assert.Single(register.Items, x => x.DocumentType == DocumentType.DebitNote);
        Assert.Equal(-5_320m, noteRow.TaxableNonCapitalLocalValue);
        Assert.Equal(-691.60m, noteRow.TaxableNonCapitalLocalVat);

        // The same note, positive, in the return register -- the same magnitudes, or the shared
        // reader is not doing its job.
        var returnRow = Assert.Single(returnRegister.Items);
        Assert.Equal(5_320m, returnRow.TaxableNonCapitalLocalValue);
        Assert.Equal(691.60m, returnRow.TaxableNonCapitalLocalVat);
    }

    private static async Task<(Guid Id, string Code)> CreateAndApproveForeignPurchaseBillAsync(
        IAppDbContext db, Seed seed, DateOnly date, decimal rate, string currencyCode, decimal exchangeRate)
    {
        var created = await new CreatePurchaseBillCommandHandler(db).Handle(
            new CreatePurchaseBillCommand(
                seed.OrganizationId, seed.SupplierId, seed.WarehouseId, date, null, null,
                false, null, null, null, null,
                [new PurchaseBillLineInput(
                    seed.ProductId, 1m, rate, VatRate.ThirteenPercentVat, ExpenditureClassification.Others)])
            {
                CurrencyCode = currencyCode,
                ExchangeRate = exchangeRate,
            },
            CancellationToken.None);

        var approved = await new ApprovePurchaseBillCommandHandler(
            db, seed.NumberGenerator, new FakeCurrentUserService(Guid.NewGuid()), new PurchaseBillPostingRule(),
            new StockLedgerService(db))
            .Handle(new ApprovePurchaseBillCommand(seed.OrganizationId, created.Id), CancellationToken.None);

        return (approved.Id, approved.Code);
    }

    private static async Task<(Guid Id, string Code)> CreateAndApproveForeignDebitNoteAsync(
        IAppDbContext db, Seed seed, DateOnly date, decimal quantity, decimal rate, string currencyCode,
        decimal exchangeRate, Guid referrerPurchaseBillId)
    {
        var created = await new CreateDebitNoteCommandHandler(db).Handle(
            new CreateDebitNoteCommand(
                seed.OrganizationId, seed.SupplierId, date, null, null,
                [new DebitNoteLineInput(seed.ProductId, quantity, rate, VatRate.ThirteenPercentVat)],
                DocumentType.PurchaseBill, referrerPurchaseBillId)
            {
                CurrencyCode = currencyCode,
                ExchangeRate = exchangeRate,
            },
            CancellationToken.None);

        var approved = await new ApproveDebitNoteCommandHandler(
            db, seed.NumberGenerator, new FakeCurrentUserService(Guid.NewGuid()),
            new DebitNotePostingRule(), new StockLedgerService(db))
            .Handle(new ApproveDebitNoteCommand(seed.OrganizationId, created.Id), CancellationToken.None);

        return (approved.Id, approved.Code);
    }

    /// <summary>
    /// Phase 44 -- <b>the Billing Location filter narrows the bills, not only the returns.</b>
    ///
    /// <para>This test fails against the code as it stood before this phase. Phase 35b gave the query
    /// its <c>LocationId</c> and taught <c>PurchaseReturnReader</c> to honour it, but never applied
    /// it to the purchase-bill half -- so choosing a location removed the debit notes from the
    /// register and left every bill in it. That is phase-34b's rule (a filter a screen displays but
    /// does not apply is worse than no filter) with a statutory register's numbers behind it.</para>
    ///
    /// <para><c>ReportLocationSweepGuardTests</c> could not see it: that guard asks whether the
    /// <i>query record</i> accepts a LocationId, which this one always did. Whether a handler applies
    /// what it accepts is not decidable by reflection, so it is pinned here instead, per report.</para>
    /// </summary>
    [Fact]
    public async Task The_location_filter_narrows_the_bills_and_not_only_the_returns()
    {
        var db = TestAppDbContext.Create();
        var seed = await SeedAsync(db);
        var (headOffice, branch) = await SeedTwoLocationsAsync(db, seed.OrganizationId);

        await CreateAndApproveLocatedPurchaseBillAsync(db, seed, new DateOnly(2026, 1, 10), 100m, headOffice);
        await CreateAndApproveLocatedPurchaseBillAsync(db, seed, new DateOnly(2026, 1, 11), 250m, branch);

        var all = await RegisterAsync(db, seed, locationId: null);
        Assert.Equal(2, all.Items.Count);
        Assert.Equal(350m, all.TotalTaxExemptValue);

        var branchOnly = await RegisterAsync(db, seed, branch);
        var row = Assert.Single(branchOnly.Items);
        Assert.Equal(250m, row.TaxExemptValue);

        // The footer follows the filter too -- it is computed over the filtered set, not the period.
        Assert.Equal(250m, branchOnly.TotalTaxExemptValue);
    }

    private static async Task<PurchaseRegisterDto> RegisterAsync(IAppDbContext db, Seed seed, Guid? locationId) =>
        await new PurchaseRegisterQueryHandler(db, new FakeCurrentUserService(Guid.NewGuid()))
            .Handle(
                new PurchaseRegisterQuery(
                    seed.OrganizationId, new DateOnly(2026, 1, 1), new DateOnly(2026, 1, 31), null)
                {
                    LocationId = locationId,
                },
                CancellationToken.None);

    private static async Task<(Guid HeadOffice, Guid Branch)> SeedTwoLocationsAsync(IAppDbContext db, Guid organizationId)
    {
        var headOffice = BillingLocation.CreateHeadOffice(organizationId);
        var branch = BillingLocation.Create(organizationId, "BR1", "Branch One", null, null);
        db.BillingLocations.Add(headOffice);
        db.BillingLocations.Add(branch);
        await db.SaveChangesAsync(CancellationToken.None);

        var settings = await db.TenantSettings.SingleAsync(x => x.OrganizationId == organizationId, CancellationToken.None);
        settings.SetLocationSettings(LocationScopeMode.AllTransactions, locationWiseReportPermission: false);
        await db.SaveChangesAsync(CancellationToken.None);

        return (headOffice.Id, branch.Id);
    }

    private static async Task CreateAndApproveLocatedPurchaseBillAsync(
        IAppDbContext db, Seed seed, DateOnly date, decimal rate, Guid locationId)
    {
        var created = await new CreatePurchaseBillCommandHandler(db).Handle(
            new CreatePurchaseBillCommand(
                seed.OrganizationId, seed.SupplierId, seed.WarehouseId, date, null, null, false, null, null, null, null,
                [new PurchaseBillLineInput(
                    seed.ProductId, 1m, rate, VatRate.NoVat, ExpenditureClassification.Others)])
            {
                LocationId = locationId,
            },
            CancellationToken.None);

        await new ApprovePurchaseBillCommandHandler(
            db, seed.NumberGenerator, new FakeCurrentUserService(Guid.NewGuid()), new PurchaseBillPostingRule(),
            new StockLedgerService(db))
            .Handle(new ApprovePurchaseBillCommand(seed.OrganizationId, created.Id), CancellationToken.None);
    }

    private static async Task<(Guid Id, string Code)> CreateAndApprovePurchaseBillAsync(
        IAppDbContext db, Seed seed, DateOnly date, bool isImport, string? importDocumentNo,
        IReadOnlyList<(Guid ProductId, decimal Quantity, decimal Rate, VatRate VatRate, ExpenditureClassification Classification)> lines)
    {
        var created = await new CreatePurchaseBillCommandHandler(db).Handle(
            new CreatePurchaseBillCommand(
                seed.OrganizationId, seed.SupplierId, seed.WarehouseId, date, null, null,
                isImport, isImport ? "Nepal" : null, isImport ? date : null, isImport ? importDocumentNo : null, null,
                lines.Select(l => new PurchaseBillLineInput(l.ProductId, l.Quantity, l.Rate, l.VatRate, l.Classification)).ToList()),
            CancellationToken.None);

        var approved = await new ApprovePurchaseBillCommandHandler(
            db, seed.NumberGenerator, new FakeCurrentUserService(Guid.NewGuid()), new PurchaseBillPostingRule(),
            new StockLedgerService(db))
            .Handle(new ApprovePurchaseBillCommand(seed.OrganizationId, created.Id), CancellationToken.None);

        return (approved.Id, approved.Code);
    }

    private static async Task<(Guid Id, string Code)> CreateAndApproveDebitNoteAsync(
        IAppDbContext db, Seed seed, DateOnly date, decimal quantity, decimal rate, Guid referrerPurchaseBillId)
    {
        var created = await new CreateDebitNoteCommandHandler(db).Handle(
            new CreateDebitNoteCommand(
                seed.OrganizationId, seed.SupplierId, date, null, null,
                [new DebitNoteLineInput(seed.ProductId, quantity, rate, VatRate.ThirteenPercentVat)],
                DocumentType.PurchaseBill, referrerPurchaseBillId),
            CancellationToken.None);

        var approved = await new ApproveDebitNoteCommandHandler(
            db, seed.NumberGenerator, new FakeCurrentUserService(Guid.NewGuid()),
            new DebitNotePostingRule(), new StockLedgerService(db))
            .Handle(new ApproveDebitNoteCommand(seed.OrganizationId, created.Id), CancellationToken.None);

        return (approved.Id, approved.Code);
    }
}
