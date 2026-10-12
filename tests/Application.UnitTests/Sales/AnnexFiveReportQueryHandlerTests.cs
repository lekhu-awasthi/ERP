using ErpApp.Application.Accounting;
using ErpApp.Application.Accounting.Commands.CreateAccount;
using ErpApp.Application.Accounting.Commands.CreateAccountGroup;
using ErpApp.Application.Catalog.Commands.CreateProduct;
using ErpApp.Application.Catalog.Commands.CreateProductCategory;
using ErpApp.Application.Catalog.Commands.CreateUnitOfMeasurement;
using ErpApp.Application.Common.Persistence;
using ErpApp.Application.Contacts.Commands.CreateContact;
using ErpApp.Application.Inventory.Stock;
using ErpApp.Application.Sales;
using ErpApp.Application.Sales.Commands.ApproveCreditNote;
using ErpApp.Application.Sales.Commands.ApproveInvoice;
using ErpApp.Application.Sales.Commands.CreateCreditNote;
using ErpApp.Application.Sales.Commands.CreateInvoice;
using ErpApp.Application.Sales.Credit;
using ErpApp.Application.Sales.Posting;
using ErpApp.Application.Sales.Queries.AnnexFiveReport;
using ErpApp.Application.Sales.Stock;
using ErpApp.Application.Tenancy.Commands.CreateWarehouse;
using ErpApp.Application.UnitTests.TestSupport;
using ErpApp.Domain.Accounting;
using ErpApp.Domain.Catalog;
using ErpApp.Domain.Common;
using ErpApp.Domain.Contacts;
using ErpApp.Domain.Sales;
using ErpApp.Domain.Identity;
using ErpApp.Domain.Tenancy;
using ErpApp.Domain.Workflow;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.UnitTests.Sales;

public class AnnexFiveReportQueryHandlerTests
{
    /// <summary>
    /// Phase 89 -- the vendor's columns this app can now back. Amount is before discount and Discount is beside it (the
    /// vendor's view: Amount 1,006.50, Discount 50, Taxable 956.50), the fiscal year is the BS one the bill falls in, the
    /// print columns come from phase 67's counted prints (the first print is the original bill), and Entered By is the
    /// user on the Create audit row, not the approver.
    /// </summary>
    [Fact]
    public async Task A_bill_row_carries_its_discount_fiscal_year_prints_and_who_entered_it()
    {
        var db = TestAppDbContext.Create();
        var seed = await SeedAsync(db);

        // 10 x 100 with a 10% line discount and a 5% header discount: 1,000 -> 900 -> 855, VAT 111.15.
        var created = await new CreateInvoiceCommandHandler(db, await SalesPricing.PricerAsync(db, seed.OrganizationId)).Handle(
            new CreateInvoiceCommand(
                seed.OrganizationId, seed.CustomerId, seed.WarehouseId, new DateOnly(2026, 1, 15), null,
                [new InvoiceLineInput(seed.ProductId, 10m, 100m, VatRate.ThirteenPercentVat, DiscountPct: 10m)],
                ReferrerType: null, ReferrerId: null, DiscountPct: 5m),
            CancellationToken.None);
        var stockLedgerService = new StockLedgerService(db);
        await new ApproveInvoiceCommandHandler(
            db, seed.NumberGenerator, new FakeCurrentUserService(Guid.NewGuid()), new InvoicePostingRule(),
            new FifoStockAvailabilityPolicy(db, stockLedgerService), stockLedgerService, new ContactCreditLimitPolicy(db))
            .Handle(new ApproveInvoiceCommand(seed.OrganizationId, created.Id, OverrideWarning: false), CancellationToken.None);

        var clerk = User.Register("Sita Clerk", "sita@example.test", "9800000001", "x");
        var cashier = User.Register("Hari Cashier", "hari@example.test", "9800000002", "x");
        db.Users.AddRange(clerk, cashier);
        db.Audits.Add(Audit.Create(seed.OrganizationId, clerk.Id, "Create", DocumentType.Invoice, created.Id));
        var invoice = await db.Invoices.SingleAsync(x => x.Id == created.Id);
        var firstPrint = new DateTimeOffset(2026, 1, 15, 10, 0, 0, TimeSpan.Zero);
        db.InvoicePrints.Add(InvoicePrint.Record(invoice, 0, cashier.Id, firstPrint, PrintMedium.Pdf));
        db.InvoicePrints.Add(InvoicePrint.Record(invoice, 1, clerk.Id, firstPrint.AddHours(2), PrintMedium.Email));
        await db.SaveChangesAsync(CancellationToken.None);

        var result = await new AnnexFiveReportQueryHandler(db, new FakeCurrentUserService(Guid.NewGuid())).Handle(
            new AnnexFiveReportQuery(seed.OrganizationId, new DateOnly(2026, 1, 1), new DateOnly(2026, 1, 31)),
            CancellationToken.None);

        var row = Assert.Single(result.Rows);
        Assert.Equal(1_000m, row.Amount);
        Assert.Equal(145m, row.Discount);
        Assert.Equal(855m, row.TaxableAmount);
        Assert.Equal(111.15m, row.TaxAmount);
        Assert.Equal(966.15m, row.TotalAmount);
        Assert.Equal(2082, row.FiscalYear);
        Assert.Equal(2, row.PrintCount);
        Assert.Equal(firstPrint, row.PrintedAt);
        Assert.Equal("Hari Cashier", row.PrintedBy);
        Assert.Equal("Sita Clerk", row.EnteredBy);
    }

    [Fact]
    public async Task Handle_computes_amount_taxable_amount_and_tax_amount_from_mixed_vat_rate_lines()
    {
        var db = TestAppDbContext.Create();
        var seed = await SeedAsync(db);

        // One Invoice with a taxable line (1,000 @ 13%) and a non-taxable line (500 @ NoVat) --
        // Amount should sum both (1,500), TaxableAmount only the 13% line (1,000), TaxAmount the VAT
        // on that line (130), TotalAmount Amount+TaxAmount (1,630). Matches the arithmetic confirmed
        // against the live Tigg screen (e.g. Amount 91,001 / Taxable_Amount 5,400 / Total_Amount
        // 91,703, where 91,703 - 91,001 = 702 = round(5,400 * 0.13)).
        var invoice = await CreateAndApproveInvoiceAsync(
            db, seed, new DateOnly(2026, 1, 15),
            [(seed.ProductId, 10m, 100m, VatRate.ThirteenPercentVat), (seed.ProductId, 5m, 100m, VatRate.NoVat)]);

        var handler = new AnnexFiveReportQueryHandler(db, new FakeCurrentUserService(Guid.NewGuid()));
        var result = await handler.Handle(
            new AnnexFiveReportQuery(seed.OrganizationId, new DateOnly(2026, 1, 1), new DateOnly(2026, 1, 31)),
            CancellationToken.None);

        var row = Assert.Single(result.Rows);
        Assert.Equal(DocumentType.Invoice, row.DocumentType);
        Assert.Equal(invoice.Code, row.BillNo);
        Assert.Equal(seed.CustomerId, row.ContactId);
        Assert.Equal(seed.CustomerPan, row.ContactPan);
        Assert.Equal(1500m, row.Amount);
        Assert.Equal(1000m, row.TaxableAmount);
        Assert.Equal(130m, row.TaxAmount);
        Assert.Equal(1630m, row.TotalAmount);
        Assert.True(row.IsActive);
    }

    [Fact]
    public async Task Handle_lists_a_credit_note_as_its_own_positive_valued_row_not_netted_against_its_source_invoice()
    {
        var db = TestAppDbContext.Create();
        var seed = await SeedAsync(db);

        await CreateAndApproveInvoiceAsync(
            db, seed, new DateOnly(2026, 1, 10), [(seed.ProductId, 10m, 100m, VatRate.ThirteenPercentVat)]);

        var creditNote = await CreateAndApproveStandaloneCreditNoteAsync(
            db, seed, new DateOnly(2026, 1, 12), 2m, 100m, VatRate.ThirteenPercentVat);

        var handler = new AnnexFiveReportQueryHandler(db, new FakeCurrentUserService(Guid.NewGuid()));
        var result = await handler.Handle(
            new AnnexFiveReportQuery(seed.OrganizationId, new DateOnly(2026, 1, 1), new DateOnly(2026, 1, 31)),
            CancellationToken.None);

        Assert.Equal(2, result.Rows.Count);

        var creditNoteRow = Assert.Single(result.Rows, r => r.DocumentType == DocumentType.CreditNote);
        Assert.Equal(creditNote.Code, creditNoteRow.BillNo);
        // Positive, not sign-flipped -- unlike TdsReportRowDto's DebitNote convention, the live Tigg
        // screen showed a CreditNote's own Amount/Total_Amount as positive values.
        Assert.Equal(200m, creditNoteRow.Amount);
        Assert.Equal(200m, creditNoteRow.TaxableAmount);
        Assert.Equal(26m, creditNoteRow.TaxAmount);
        Assert.Equal(226m, creditNoteRow.TotalAmount);
        Assert.True(creditNoteRow.IsActive);
    }

    [Fact]
    public async Task Handle_excludes_draft_and_out_of_range_documents()
    {
        var db = TestAppDbContext.Create();
        var seed = await SeedAsync(db);

        var inRange = await CreateAndApproveInvoiceAsync(
            db, seed, new DateOnly(2026, 1, 15), [(seed.ProductId, 1m, 100m, VatRate.NoVat)]);
        var draft = await CreateInvoiceAsync(
            db, seed, new DateOnly(2026, 1, 16), [(seed.ProductId, 1m, 100m, VatRate.NoVat)]);
        var outOfRange = await CreateAndApproveInvoiceAsync(
            db, seed, new DateOnly(2026, 3, 1), [(seed.ProductId, 1m, 100m, VatRate.NoVat)]);

        var handler = new AnnexFiveReportQueryHandler(db, new FakeCurrentUserService(Guid.NewGuid()));
        var result = await handler.Handle(
            new AnnexFiveReportQuery(seed.OrganizationId, new DateOnly(2026, 1, 1), new DateOnly(2026, 1, 31)),
            CancellationToken.None);

        var row = Assert.Single(result.Rows);
        Assert.Equal(inRange.Code, row.BillNo);
        Assert.DoesNotContain(result.Rows, r => r.BillNo == draft.Code);
        Assert.DoesNotContain(result.Rows, r => r.BillNo == outOfRange.Code);
    }

    private sealed record Seed(
        Guid OrganizationId, FakeDocumentNumberGenerator NumberGenerator, Guid CustomerId, string? CustomerPan,
        Guid WarehouseId, Guid ProductId);

    private static async Task<Seed> SeedAsync(IAppDbContext db)
    {
        var organizationId = Guid.NewGuid();
        var numberGenerator = new FakeDocumentNumberGenerator();

        const string customerPan = "609876543";
        var customer = await new CreateContactCommandHandler(db, numberGenerator).Handle(
            new CreateContactCommand(
                organizationId, ContactType.Customer, "Acme Traders", null, customerPan, null, null, null, 0m),
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

        return new Seed(organizationId, numberGenerator, customer.Id, customerPan, warehouse.Id, product.Id);
    }

    private static async Task<CreateInvoiceResult> CreateInvoiceAsync(
        IAppDbContext db, Seed seed, DateOnly date,
        IReadOnlyList<(Guid ProductId, decimal Quantity, decimal Rate, VatRate VatRate)> lines)
    {
        return await new CreateInvoiceCommandHandler(db, await SalesPricing.PricerAsync(db, seed.OrganizationId)).Handle(
            new CreateInvoiceCommand(
                seed.OrganizationId, seed.CustomerId, seed.WarehouseId, date, null,
                lines.Select(l => new InvoiceLineInput(l.ProductId, l.Quantity, l.Rate, l.VatRate)).ToList()),
            CancellationToken.None);
    }

    private static async Task<(Guid Id, string Code)> CreateAndApproveInvoiceAsync(
        IAppDbContext db, Seed seed, DateOnly date,
        IReadOnlyList<(Guid ProductId, decimal Quantity, decimal Rate, VatRate VatRate)> lines)
    {
        var created = await CreateInvoiceAsync(db, seed, date, lines);
        var stockLedgerService = new StockLedgerService(db);
        var approved = await new ApproveInvoiceCommandHandler(
            db, seed.NumberGenerator, new FakeCurrentUserService(Guid.NewGuid()), new InvoicePostingRule(),
            new FifoStockAvailabilityPolicy(db, stockLedgerService), stockLedgerService, new ContactCreditLimitPolicy(db))
            .Handle(new ApproveInvoiceCommand(seed.OrganizationId, created.Id, OverrideWarning: false), CancellationToken.None);
        return (approved.Id, approved.Code);
    }

    private static async Task<(Guid Id, string Code)> CreateAndApproveStandaloneCreditNoteAsync(
        IAppDbContext db, Seed seed, DateOnly date, decimal quantity, decimal rate, VatRate vatRate)
    {
        var created = await new CreateCreditNoteCommandHandler(db).Handle(
            new CreateCreditNoteCommand(
                seed.OrganizationId, seed.CustomerId, date, null,
                [new CreditNoteLineInput(seed.ProductId, quantity, rate, vatRate)]),
            CancellationToken.None);

        var approved = await new ApproveCreditNoteCommandHandler(
            db, seed.NumberGenerator, new FakeCurrentUserService(Guid.NewGuid()),
            new CreditNotePostingRule(), new StockLedgerService(db))
            .Handle(new ApproveCreditNoteCommand(seed.OrganizationId, created.Id), CancellationToken.None);

        return (approved.Id, approved.Code);
    }
}
