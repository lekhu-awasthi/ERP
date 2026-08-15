using ErpApp.Application.Accounting;
using ErpApp.Application.Accounting.Commands.CreateAccount;
using ErpApp.Application.Accounting.Commands.CreateAccountGroup;
using ErpApp.Application.Catalog.Commands.CreateProduct;
using ErpApp.Application.Catalog.Commands.CreateProductCategory;
using ErpApp.Application.Catalog.Commands.CreateUnitOfMeasurement;
using ErpApp.Application.Common.Persistence;
using ErpApp.Application.Configuration.Commands.CreateTdsType;
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
using ErpApp.Application.Purchasing.Queries.TdsReport;
using ErpApp.Application.Tenancy.Commands.CreateWarehouse;
using ErpApp.Application.UnitTests.TestSupport;
using ErpApp.Domain.Accounting;
using ErpApp.Domain.Catalog;
using ErpApp.Domain.Common;
using ErpApp.Domain.Contacts;
using ErpApp.Domain.Purchasing;
using ErpApp.Domain.Tenancy;

namespace ErpApp.Application.UnitTests.Purchasing;

public class TdsReportQueryHandlerTests
{
    [Fact]
    public async Task Handle_returns_rows_only_for_approved_documents_carrying_a_tds_type_within_range()
    {
        var db = TestAppDbContext.Create();
        var seed = await SeedAsync(db);

        // PurchaseBill with TDS, in range -- gross 1000+130=1130 (13% VAT), TDS base is pre-VAT
        // 1000 * 15% = 150, net payable 980.
        var billWithTds = await CreateAndApprovePurchaseBillAsync(
            db, seed, new DateOnly(2026, 1, 10), 10m, 100m, VatRate.ThirteenPercentVat, seed.TdsTypeId);

        // PurchaseBill with no TDS at all -- TdsTypeId null, so no deduction happened; must be
        // excluded entirely, not shown as a zero-TDS row.
        await CreateAndApprovePurchaseBillAsync(db, seed, new DateOnly(2026, 1, 11), 1m, 50m, VatRate.NoVat, null);

        // Draft PurchaseBill with TDS, in range -- must be excluded, Approved-only.
        await CreatePurchaseBillAsync(db, seed, new DateOnly(2026, 1, 12), 1m, 50m, VatRate.NoVat, seed.TdsTypeId);

        // Approved PurchaseBill with TDS but outside the query's date range -- must be excluded.
        await CreateAndApprovePurchaseBillAsync(
            db, seed, new DateOnly(2026, 3, 1), 1m, 50m, VatRate.NoVat, seed.TdsTypeId);

        // Expense with TDS, in range -- gross 500 (NoVat), TDS base 500 * 15% = 75, net 425.
        var expenseWithTds = await CreateAndApproveExpenseAsync(db, seed, new DateOnly(2026, 1, 20), 500m, seed.TdsTypeId);

        var handler = new TdsReportQueryHandler(db);
        var result = await handler.Handle(
            new TdsReportQuery(seed.OrganizationId, new DateOnly(2026, 1, 1), new DateOnly(2026, 1, 31)),
            CancellationToken.None);

        Assert.Equal(2, result.Rows.Count);

        var billRow = Assert.Single(result.Rows, r => r.DocumentType == DocumentType.PurchaseBill);
        Assert.Equal(billWithTds.Code, billRow.EntryNo);
        Assert.Equal(seed.SupplierId, billRow.ContactId);
        Assert.Equal(seed.SupplierPan, billRow.ContactPan);
        Assert.Equal(seed.TdsTypeCode, billRow.TdsTypeCode);
        Assert.Equal(15m, billRow.TdsRatePct);
        Assert.Equal(1130m, billRow.GrossAmount);
        Assert.Equal(150m, billRow.TdsAmount);
        Assert.Equal(980m, billRow.NetPayableAmount);

        var expenseRow = Assert.Single(result.Rows, r => r.DocumentType == DocumentType.Expense);
        Assert.Equal(expenseWithTds.Code, expenseRow.EntryNo);
        Assert.Equal(500m, expenseRow.GrossAmount);
        Assert.Equal(75m, expenseRow.TdsAmount);
        Assert.Equal(425m, expenseRow.NetPayableAmount);

        Assert.Equal(1630m, result.TotalGrossAmount);
        Assert.Equal(225m, result.TotalTdsAmount);
    }

    [Fact]
    public async Task Handle_lists_a_debit_note_reversal_as_its_own_negative_signed_row_that_nets_the_totals_footer()
    {
        var db = TestAppDbContext.Create();
        var seed = await SeedAsync(db);

        // Source PurchaseBill: 10 x 100 @ 13% VAT with TDS -- gross 1130, TDS 150.
        var bill = await CreateAndApprovePurchaseBillAsync(
            db, seed, new DateOnly(2026, 1, 10), 10m, 100m, VatRate.ThirteenPercentVat, seed.TdsTypeId);

        // DebitNote reversing 3 of those units, same TDS type -- gross 339, TDS base 300 * 15% = 45.
        var debitNote = await CreateAndApproveDebitNoteAsync(
            db, seed, new DateOnly(2026, 1, 15), 3m, 100m, VatRate.ThirteenPercentVat, seed.TdsTypeId, bill.Id);

        var handler = new TdsReportQueryHandler(db);
        var result = await handler.Handle(
            new TdsReportQuery(seed.OrganizationId, new DateOnly(2026, 1, 1), new DateOnly(2026, 1, 31)),
            CancellationToken.None);

        Assert.Equal(2, result.Rows.Count);

        var billRow = Assert.Single(result.Rows, r => r.DocumentType == DocumentType.PurchaseBill);
        Assert.Equal(1130m, billRow.GrossAmount);
        Assert.Equal(150m, billRow.TdsAmount);

        // The reversal is listed as its own row, signed negative -- not netted away into the
        // PurchaseBill's row -- so a filer can see the reversal actually happened this period.
        var debitNoteRow = Assert.Single(result.Rows, r => r.DocumentType == DocumentType.DebitNote);
        Assert.Equal(debitNote.Code, debitNoteRow.EntryNo);
        Assert.Equal(-339m, debitNoteRow.GrossAmount);
        Assert.Equal(-45m, debitNoteRow.TdsAmount);
        Assert.Equal(-294m, debitNoteRow.NetPayableAmount);

        // But the totals footer still nets correctly, since the negative row sums straight in.
        Assert.Equal(791m, result.TotalGrossAmount);
        Assert.Equal(105m, result.TotalTdsAmount);
    }

    // See PurchaseMasterReportQueryHandlerTests' matching comment: a single FakeDocumentNumberGenerator
    // instance is shared across every Create/Approve call in one test, or every approved document
    // collides on the same "Foo-0001" code.
    private sealed record Seed(
        Guid OrganizationId, FakeDocumentNumberGenerator NumberGenerator, Guid SupplierId, string? SupplierPan,
        Guid WarehouseId, Guid ProductId, Guid ExpenseAccountId, Guid TdsTypeId, string TdsTypeCode);

    private static async Task<Seed> SeedAsync(IAppDbContext db)
    {
        var organizationId = Guid.NewGuid();
        var numberGenerator = new FakeDocumentNumberGenerator();

        var supplier = await new CreateContactCommandHandler(db, numberGenerator).Handle(
            new CreateContactCommand(
                organizationId, ContactType.Supplier, "Global Supplies", null, "301234567", null, null, null, 0m),
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
        var tdsPayable = await new CreateAccountCommandHandler(db, numberGenerator).Handle(
            new CreateAccountCommand(organizationId, "TDS Payable", liabilityGroup.Id), CancellationToken.None);
        var purchase = await new CreateAccountCommandHandler(db, numberGenerator).Handle(
            new CreateAccountCommand(organizationId, "Purchase Expense", expenseGroup.Id), CancellationToken.None);
        var officeExpense = await new CreateAccountCommandHandler(db, numberGenerator).Handle(
            new CreateAccountCommand(organizationId, "Office Expense", expenseGroup.Id), CancellationToken.None);

        var settings = TenantSettings.CreateDefault(organizationId);
        settings.SetAccountingDefaults(null, null, null, purchase.Id, ap.Id, vatReceivable.Id, tdsPayable.Id);
        db.TenantSettings.Add(settings);
        await db.SaveChangesAsync(CancellationToken.None);

        var tdsType = await new CreateTdsTypeCommandHandler(db).Handle(
            new CreateTdsTypeCommand(organizationId, "TDS-15", "Consulting Services", 15m), CancellationToken.None);

        return new Seed(
            organizationId, numberGenerator, supplier.Id, "301234567", warehouse.Id,
            product.Id, officeExpense.Id, tdsType.Id, tdsType.Code);
    }

    private static async Task<CreatePurchaseBillResult> CreatePurchaseBillAsync(
        IAppDbContext db, Seed seed, DateOnly date, decimal quantity, decimal rate, VatRate vatRate, Guid? tdsTypeId)
    {
        return await new CreatePurchaseBillCommandHandler(db).Handle(
            new CreatePurchaseBillCommand(
                seed.OrganizationId, seed.SupplierId, seed.WarehouseId, date, null, null, false, null, null, null, tdsTypeId,
                [new PurchaseBillLineInput(seed.ProductId, quantity, rate, vatRate, ExpenditureClassification.Others)]),
            CancellationToken.None);
    }

    private static async Task<(Guid Id, string Code)> CreateAndApprovePurchaseBillAsync(
        IAppDbContext db, Seed seed, DateOnly date, decimal quantity, decimal rate, VatRate vatRate, Guid? tdsTypeId)
    {
        var created = await CreatePurchaseBillAsync(db, seed, date, quantity, rate, vatRate, tdsTypeId);
        var approved = await new ApprovePurchaseBillCommandHandler(
            db, seed.NumberGenerator, new FakeCurrentUserService(Guid.NewGuid()), new PurchaseBillPostingRule(),
            new StockLedgerService(db))
            .Handle(new ApprovePurchaseBillCommand(seed.OrganizationId, created.Id), CancellationToken.None);
        return (approved.Id, approved.Code);
    }

    private static async Task<(Guid Id, string Code)> CreateAndApproveExpenseAsync(
        IAppDbContext db, Seed seed, DateOnly date, decimal amount, Guid? tdsTypeId)
    {
        var created = await new CreateExpenseCommandHandler(db).Handle(
            new CreateExpenseCommand(
                seed.OrganizationId, seed.SupplierId, date, null, null, null, true, tdsTypeId,
                [new ExpenseLineInput(seed.ExpenseAccountId, amount, VatRate.NoVat)]),
            CancellationToken.None);

        var approved = await new ApproveExpenseCommandHandler(
            db, seed.NumberGenerator, new FakeCurrentUserService(Guid.NewGuid()), new ExpensePostingRule())
            .Handle(new ApproveExpenseCommand(seed.OrganizationId, created.Id), CancellationToken.None);

        return (approved.Id, approved.Code);
    }

    private static async Task<(Guid Id, string Code)> CreateAndApproveDebitNoteAsync(
        IAppDbContext db, Seed seed, DateOnly date, decimal quantity, decimal rate, VatRate vatRate, Guid? tdsTypeId,
        Guid referrerPurchaseBillId)
    {
        var created = await new CreateDebitNoteCommandHandler(db).Handle(
            new CreateDebitNoteCommand(
                seed.OrganizationId, seed.SupplierId, date, null, tdsTypeId,
                [new DebitNoteLineInput(seed.ProductId, quantity, rate, vatRate)],
                DocumentType.PurchaseBill, referrerPurchaseBillId),
            CancellationToken.None);

        var approved = await new ApproveDebitNoteCommandHandler(
            db, seed.NumberGenerator, new FakeCurrentUserService(Guid.NewGuid()),
            new DebitNotePostingRule(), new StockLedgerService(db))
            .Handle(new ApproveDebitNoteCommand(seed.OrganizationId, created.Id), CancellationToken.None);

        return (approved.Id, approved.Code);
    }
}
