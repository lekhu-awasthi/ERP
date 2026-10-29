using ErpApp.Application.Accounting.Commands.CreateAccount;
using ErpApp.Application.Accounting.Commands.CreateAccountGroup;
using ErpApp.Application.Accounting.Queries.VatSummaryReport;
using ErpApp.Application.Catalog.Commands.CreateProduct;
using ErpApp.Application.Catalog.Commands.CreateProductCategory;
using ErpApp.Application.Catalog.Commands.CreateUnitOfMeasurement;
using ErpApp.Application.Catalog.Commands.UpdateProduct;
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
using ErpApp.Application.Purchasing.Commands.VoidDebitNote;
using ErpApp.Application.Purchasing.Commands.VoidExpense;
using ErpApp.Application.Purchasing.Commands.VoidPurchaseBill;
using ErpApp.Application.Purchasing.Posting;
using ErpApp.Application.Purchasing.Queries.GetPurchaseBill;
using ErpApp.Application.Purchasing.Queries.PreviewPurchaseBillGlPosting;
using ErpApp.Application.Sales;
using ErpApp.Application.Sales.Commands.ApproveInvoice;
using ErpApp.Application.Sales.Commands.CreateInvoice;
using ErpApp.Application.Sales.Credit;
using ErpApp.Application.Sales.Posting;
using ErpApp.Application.Sales.Stock;
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

/// <summary>
/// Phase 99 -- input VAT apportionment (VAT Rules 40 and 41). Exempt use and Rule 41 are posted at approval to the line's
/// own account; a shared line keeps its whole credit in the ledger and the VAT return works out the part not allowed from
/// the month's sales. Every figure below is worked by hand in the comment beside it.
/// </summary>
public class InputVatApportionmentTests
{
    private const int Fy = 2082;

    private static DateOnly Bs(int year, int month, int day) => BsCalendar.ToGregorian(new BsDate(year, month, day))!.Value;

    [Fact]
    public async Task A_bill_posts_exempt_use_and_Rule_41_to_the_lines_account_and_the_rest_to_VAT_Receivable()
    {
        var db = TestAppDbContext.Create();
        var seed = await SeedAsync(db);

        var billId = await CreateAndApproveBillAsync(db, seed, Bs(2082, 6, 10),
        [
            Line(seed.ServiceId, 1_000m, VatCreditUse.Taxable, VatCreditCategory.Full),     // VAT 130, all credit
            Line(seed.ServiceId, 1_000m, VatCreditUse.Exempt, VatCreditCategory.Full),      // VAT 130, none
            Line(seed.ServiceId, 1_000m, VatCreditUse.Shared, VatCreditCategory.Full),      // VAT 130, all at approval
            Line(seed.ServiceId, 1_000m, VatCreditUse.Taxable, VatCreditCategory.Vehicle),  // VAT 130, 52 credit, 78 not
            Line(seed.ServiceId, 1_000m, VatCreditUse.Taxable, VatCreditCategory.Computer), // VAT 130, 78 credit, 52 not
            Line(seed.ServiceId, 1_000m, VatCreditUse.Taxable, VatCreditCategory.Blocked),  // VAT 130, none
        ]);

        var lines = await GlLinesAsync(db, DocumentType.PurchaseBill, billId);
        // 6,000 of purchases + the disallowed 130 + 78 + 52 + 130 = 6,390.
        Assert.Equal(6_390m, Net(lines, seed.PurchaseAccountId));
        // 130 + 130 + 52 + 78 = 390.
        Assert.Equal(390m, Net(lines, seed.VatReceivableId));
        // The supplier is owed the whole bill: 6,000 + 780.
        Assert.Equal(-6_780m, Net(lines, seed.PayableId));
        Assert.Equal(lines.Sum(x => x.Debit), lines.Sum(x => x.Credit));

        var detail = await new GetPurchaseBillQueryHandler(db).Handle(
            new GetPurchaseBillQuery(seed.OrganizationId, billId), CancellationToken.None);
        Assert.Equal([0m, 130m, 0m, 78m, 52m, 130m], detail.Lines.Select(x => x.DisallowedVat));
    }

    [Fact]
    public async Task A_line_takes_its_products_Rule_41_category_unless_it_names_one()
    {
        var db = TestAppDbContext.Create();
        var seed = await SeedAsync(db);
        await SetCategoryAsync(db, seed, seed.ServiceId, VatCreditCategory.Computer);

        var billId = await CreateBillAsync(db, seed, Bs(2082, 6, 10),
        [
            new PurchaseBillLineInput(seed.ServiceId, 1m, 1_000m, VatRate.ThirteenPercentVat, ExpenditureClassification.Others),
            Line(seed.ServiceId, 1_000m, VatCreditUse.Taxable, VatCreditCategory.Full),
        ]);

        var bill = await db.PurchaseBills.Include(x => x.Lines).SingleAsync(x => x.Id == billId);
        Assert.Equal(VatCreditCategory.Computer, bill.Lines.First().CreditCategory);
        Assert.Equal(VatCreditUse.Taxable, bill.Lines.First().CreditUse);
        Assert.Equal(VatCreditCategory.Full, bill.Lines.Last().CreditCategory);
    }

    [Fact]
    public async Task The_preview_posts_what_Approve_posts()
    {
        var db = TestAppDbContext.Create();
        var seed = await SeedAsync(db);
        await SetCategoryAsync(db, seed, seed.ServiceId, VatCreditCategory.Vehicle);
        PurchaseBillLineInput[] lines =
        [
            new(seed.ServiceId, 1m, 1_000m, VatRate.ThirteenPercentVat, ExpenditureClassification.Others),
            Line(seed.ServiceId, 500m, VatCreditUse.Exempt, VatCreditCategory.Full),
            Line(seed.ServiceId, 700m, VatCreditUse.Shared, VatCreditCategory.Computer),
        ];

        var preview = await new PreviewPurchaseBillGlPostingQueryHandler(db, new PurchaseBillPostingRule()).Handle(
            new PreviewPurchaseBillGlPostingQuery(seed.OrganizationId, lines, null), CancellationToken.None);
        var billId = await CreateAndApproveBillAsync(db, seed, Bs(2082, 6, 10), lines);
        var posted = await GlLinesAsync(db, DocumentType.PurchaseBill, billId);

        Assert.Equal(
            preview.OrderBy(x => x.AccountId).ThenBy(x => x.Debit).Select(x => (x.AccountId, x.Debit, x.Credit)),
            posted.OrderBy(x => x.AccountId).ThenBy(x => x.Debit).Select(x => (x.AccountId, x.Debit, x.Credit)));
    }

    [Fact]
    public async Task A_debit_note_inherits_its_bill_lines_terms_and_a_full_return_nets_every_account_to_zero()
    {
        var db = TestAppDbContext.Create();
        var seed = await SeedAsync(db);
        var billId = await CreateAndApproveBillAsync(db, seed, Bs(2082, 6, 10),
        [
            Line(seed.ServiceId, 1_000m, VatCreditUse.Exempt, VatCreditCategory.Full),
            Line(seed.ServiceId, 2_000m, VatCreditUse.Taxable, VatCreditCategory.Vehicle),
        ]);

        // The note names no terms; it inherits them by (product, rate, VAT rate).
        var noteId = await CreateAndApproveDebitNoteAsync(db, seed, Bs(2082, 6, 12), billId,
        [
            new DebitNoteLineInput(seed.ServiceId, 1m, 1_000m, VatRate.ThirteenPercentVat),
            new DebitNoteLineInput(seed.ServiceId, 1m, 2_000m, VatRate.ThirteenPercentVat),
        ]);

        var note = await GlLinesAsync(db, DocumentType.DebitNote, noteId);
        // Credits the purchase account 1,000 + 130 and 2,000 + 156 (260 x 60%), and VAT Receivable only 104.
        Assert.Equal(-3_286m, Net(note, seed.PurchaseAccountId));
        Assert.Equal(-104m, Net(note, seed.VatReceivableId));

        var both = (await GlLinesAsync(db, DocumentType.PurchaseBill, billId)).Concat(note).ToList();
        foreach (var account in new[] { seed.PurchaseAccountId, seed.VatReceivableId, seed.PayableId })
        {
            Assert.Equal(0m, Net(both, account));
        }
    }

    [Fact]
    public async Task A_goods_lines_disallowed_VAT_is_in_its_layer_and_a_return_and_a_void_each_take_back_exactly_that()
    {
        var db = TestAppDbContext.Create();
        var seed = await SeedAsync(db);
        await SetCategoryAsync(db, seed, seed.GoodsId, VatCreditCategory.Vehicle);

        // A car for 10,000 + VAT 1,300, of which 780 is not credit: Inventory 10,780, the layer 10,780.
        var billId = await CreateAndApproveBillAsync(db, seed, Bs(2082, 6, 10),
            [new PurchaseBillLineInput(seed.GoodsId, 1m, 10_000m, VatRate.ThirteenPercentVat, ExpenditureClassification.Capital)]);
        var bill = await GlLinesAsync(db, DocumentType.PurchaseBill, billId);
        Assert.Equal(10_780m, Net(bill, seed.InventoryId));
        Assert.Equal(520m, Net(bill, seed.VatReceivableId));
        var layer = await db.StockLedgerEntries.SingleAsync(x => x.SourceDocumentId == billId);
        Assert.Equal(10_780m, layer.UnitCost);

        var noteId = await CreateAndApproveDebitNoteAsync(db, seed, Bs(2082, 6, 12), billId,
            [new DebitNoteLineInput(seed.GoodsId, 1m, 10_000m, VatRate.ThirteenPercentVat)], seed.WarehouseId);
        var note = await GlLinesAsync(db, DocumentType.DebitNote, noteId);
        Assert.Equal(-10_780m, Net(note, seed.InventoryId));
        Assert.Equal(-520m, Net(note, seed.VatReceivableId));
        // Returned at its own price, so no difference for Inventory Adjustment to take.
        Assert.Equal(0m, Net(note, seed.InventoryAdjustmentId));

        // The return voided puts the car back at 10,780, so the shelf holds one car at its bill's cost again.
        await new VoidDebitNoteCommandHandler(db, new FakeCurrentUserService(Guid.NewGuid()), new StockLedgerService(db))
            .Handle(new VoidDebitNoteCommand(seed.OrganizationId, noteId), CancellationToken.None);
        var voidedNote = await GlLinesAsync(db, DocumentType.DebitNote, noteId);
        foreach (var account in new[] { seed.InventoryId, seed.VatReceivableId, seed.PayableId })
        {
            Assert.Equal(0m, Net(voidedNote, account));
        }

        var onHand = await db.StockLedgerEntries.Where(x => x.ProductId == seed.GoodsId && x.QuantityRemaining > 0).ToListAsync();
        Assert.Equal(10_780m, Assert.Single(onHand).UnitCost);

        // A second car's bill, voided before anything takes it: every account and the shelf end where they began.
        var secondId = await CreateAndApproveBillAsync(db, seed, Bs(2082, 6, 14),
            [new PurchaseBillLineInput(seed.GoodsId, 1m, 10_000m, VatRate.ThirteenPercentVat, ExpenditureClassification.Capital)]);
        await new VoidPurchaseBillCommandHandler(db, new FakeCurrentUserService(Guid.NewGuid()), new StockLedgerService(db))
            .Handle(new VoidPurchaseBillCommand(seed.OrganizationId, secondId), CancellationToken.None);
        var voidedBill = await GlLinesAsync(db, DocumentType.PurchaseBill, secondId);
        Assert.Equal(4, voidedBill.Count(x => x.AccountId == seed.InventoryId || x.AccountId == seed.VatReceivableId));
        foreach (var account in new[] { seed.InventoryId, seed.VatReceivableId, seed.PayableId })
        {
            Assert.Equal(0m, Net(voidedBill, account));
        }

        Assert.Equal(0m, await db.StockLedgerEntries
            .Where(x => x.SourceDocumentId == secondId).SumAsync(x => x.QuantityRemaining));
    }

    [Fact]
    public async Task An_expense_posts_its_disallowed_VAT_to_its_own_account_and_its_void_reverses_it()
    {
        var db = TestAppDbContext.Create();
        var seed = await SeedAsync(db);

        // Entertainment: 500 + VAT 65, none of it credit.
        var expenseId = await CreateAndApproveExpenseAsync(db, seed, Bs(2082, 6, 15),
            new ExpenseLineInput(seed.PurchaseAccountId, 500m, VatRate.ThirteenPercentVat, VatCreditUse.Taxable, VatCreditCategory.Blocked));

        var lines = await GlLinesAsync(db, DocumentType.Expense, expenseId);
        Assert.Equal(565m, Net(lines, seed.PurchaseAccountId));
        Assert.DoesNotContain(lines, x => x.AccountId == seed.VatReceivableId);

        await new VoidExpenseCommandHandler(db, new FakeCurrentUserService(Guid.NewGuid()))
            .Handle(new VoidExpenseCommand(seed.OrganizationId, expenseId), CancellationToken.None);
        var all = await db.GlJournalEntries.Include(x => x.Lines)
            .Where(x => x.SourceDocumentId == expenseId).SelectMany(x => x.Lines).ToListAsync();
        Assert.Equal(0m, Net(all, seed.PurchaseAccountId));
        Assert.Equal(0m, Net(all, seed.PayableId));
    }

    /// <summary>
    /// The month worked by hand. Sales: 6,000 taxable (VAT 780), 2,000 zero-rated, 2,000 exempt, so the taxable share is
    /// 8,000 / 10,000 = 80%. Purchases: taxable-use 1,000, exempt-use 1,000, shared 1,000, a vehicle 1,000 (each VAT 130)
    /// and an entertainment expense 500 (VAT 65). Not credit: exempt use 130, Rule 41 78 + 65 = 143, shared 130 x 20% = 26,
    /// so 299 in all. The month's VAT is 780 - (585 - 299) = 494. VAT Receivable holds 585 - 130 - 143 = 312, which is the
    /// return's allowed credit 286 plus the 26 the accountant moves by journal.
    /// </summary>
    [Fact]
    public async Task The_return_shows_the_working_and_pays_on_the_allowed_credit_only()
    {
        var db = TestAppDbContext.Create();
        var seed = await SeedAsync(db);
        var day = Bs(2082, 6, 5);
        const int aswin = 2;

        await CreateAndApproveInvoiceAsync(db, seed, day, 6_000m, VatRate.ThirteenPercentVat);
        await CreateAndApproveInvoiceAsync(db, seed, day, 2_000m, VatRate.ZeroVat);
        await CreateAndApproveInvoiceAsync(db, seed, day, 2_000m, VatRate.NoVat);
        var billId = await CreateAndApproveBillAsync(db, seed, day,
        [
            Line(seed.ServiceId, 1_000m, VatCreditUse.Taxable, VatCreditCategory.Full),
            Line(seed.ServiceId, 1_000m, VatCreditUse.Exempt, VatCreditCategory.Full),
            Line(seed.ServiceId, 1_000m, VatCreditUse.Shared, VatCreditCategory.Full),
            Line(seed.ServiceId, 1_000m, VatCreditUse.Taxable, VatCreditCategory.Vehicle),
        ]);
        var expenseId = await CreateAndApproveExpenseAsync(db, seed, day,
            new ExpenseLineInput(seed.PurchaseAccountId, 500m, VatRate.ThirteenPercentVat, VatCreditUse.Taxable, VatCreditCategory.Blocked));

        var report = await ReturnAsync(db, seed);

        Assert.Equal(130m, Monthly(report, VatReturnLines.ExemptUseVat)[aswin]);
        Assert.Equal(143m, Monthly(report, VatReturnLines.BlockedVat)[aswin]);
        Assert.Equal(130m, Monthly(report, VatReturnLines.SharedVat)[aswin]);
        Assert.Equal(80m, Monthly(report, VatReturnLines.TaxableShare)[aswin]);
        Assert.Equal(26m, Monthly(report, VatReturnLines.SharedVatDisallowed)[aswin]);
        Assert.Equal(299m, Monthly(report, VatReturnLines.DisallowedInputVat)[aswin]);
        Assert.Equal(494m, Monthly(report, VatReturnLines.MonthVat)[aswin]);
        Assert.Equal(494m, Monthly(report, VatReturnLines.Payable)[aswin]);
        Assert.True(Line(report, VatReturnLines.TaxableShare).IsPercent);
        Assert.Null(Line(report, VatReturnLines.TaxableShare).Total);
        // The input VAT lines are still the purchase book's, unreduced.
        Assert.Equal(585m, Monthly(report, VatReturnLines.TaxablePurchasesVat)[aswin]);

        var ledger = (await GlLinesAsync(db, DocumentType.PurchaseBill, billId))
            .Concat(await GlLinesAsync(db, DocumentType.Expense, expenseId)).ToList();
        Assert.Equal(312m, Net(ledger, seed.VatReceivableId));
        Assert.Equal(
            Net(ledger, seed.VatReceivableId) - Monthly(report, VatReturnLines.SharedVatDisallowed)[aswin],
            Monthly(report, VatReturnLines.TaxablePurchasesVat)[aswin] - Monthly(report, VatReturnLines.DisallowedInputVat)[aswin]);
    }

    [Fact]
    public async Task A_return_of_an_apportioned_line_takes_its_disallowance_back_in_its_month()
    {
        var db = TestAppDbContext.Create();
        var seed = await SeedAsync(db);
        var day = Bs(2082, 6, 5);
        const int aswin = 2;

        // Half the sales exempt: the shared line keeps half its credit.
        await CreateAndApproveInvoiceAsync(db, seed, day, 1_000m, VatRate.ThirteenPercentVat);
        await CreateAndApproveInvoiceAsync(db, seed, day, 1_000m, VatRate.NoVat);
        var billId = await CreateAndApproveBillAsync(db, seed, day,
        [
            Line(seed.ServiceId, 1_000m, VatCreditUse.Exempt, VatCreditCategory.Full),
            Line(seed.ServiceId, 2_000m, VatCreditUse.Shared, VatCreditCategory.Full),
        ]);
        // Half of each line goes back the same month.
        await CreateAndApproveDebitNoteAsync(db, seed, day.AddDays(2), billId,
        [
            new DebitNoteLineInput(seed.ServiceId, 0.5m, 1_000m, VatRate.ThirteenPercentVat),
            new DebitNoteLineInput(seed.ServiceId, 0.5m, 2_000m, VatRate.ThirteenPercentVat),
        ]);

        var report = await ReturnAsync(db, seed);

        Assert.Equal(65m, Monthly(report, VatReturnLines.ExemptUseVat)[aswin]);   // 130 - 65
        Assert.Equal(130m, Monthly(report, VatReturnLines.SharedVat)[aswin]);     // 260 - 130
        Assert.Equal(50m, Monthly(report, VatReturnLines.TaxableShare)[aswin]);
        Assert.Equal(65m, Monthly(report, VatReturnLines.SharedVatDisallowed)[aswin]);
        // Output 130 - (input 390 - returns 195 - not credit 130) = 65.
        Assert.Equal(65m, Monthly(report, VatReturnLines.MonthVat)[aswin]);
    }

    [Fact]
    public async Task A_business_with_no_exempt_sales_sees_no_change_and_a_month_without_sales_keeps_its_credit()
    {
        var db = TestAppDbContext.Create();
        var seed = await SeedAsync(db);

        // Shrawan: taxable sales only. Bhadra: no sales at all. A shared purchase in each.
        await CreateAndApproveInvoiceAsync(db, seed, Bs(2082, 4, 5), 1_000m, VatRate.ThirteenPercentVat);
        await CreateAndApproveBillAsync(db, seed, Bs(2082, 4, 6), [Line(seed.ServiceId, 1_000m, VatCreditUse.Shared, VatCreditCategory.Full)]);
        await CreateAndApproveBillAsync(db, seed, Bs(2082, 5, 6), [Line(seed.ServiceId, 1_000m, VatCreditUse.Shared, VatCreditCategory.Full)]);

        var report = await ReturnAsync(db, seed);

        Assert.Equal(100m, Monthly(report, VatReturnLines.TaxableShare)[0]);
        Assert.Equal(0m, Monthly(report, VatReturnLines.DisallowedInputVat)[0]);
        Assert.Equal(0m, Monthly(report, VatReturnLines.MonthVat)[0]);          // 130 - 130
        // Nothing to apportion against: the share applied is all of it, and the 130 is a credit.
        Assert.Equal(100m, Monthly(report, VatReturnLines.TaxableShare)[1]);
        Assert.Equal(0m, Monthly(report, VatReturnLines.SharedVatDisallowed)[1]);
        Assert.Equal(-130m, Monthly(report, VatReturnLines.MonthVat)[1]);
    }

    [Fact]
    public async Task Every_validator_refuses_an_unknown_credit_term()
    {
        var bill = new CreatePurchaseBillCommandValidator().Validate(new CreatePurchaseBillCommand(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), new DateOnly(2026, 1, 1), null, null, false, null, null, null, null,
            [new PurchaseBillLineInput(Guid.NewGuid(), 1m, 1m, VatRate.NoVat, ExpenditureClassification.Others, CreditUse: (VatCreditUse)9)]));
        Assert.Contains(bill.Errors, x => x.PropertyName.EndsWith("CreditUse", StringComparison.Ordinal));

        var expense = new CreateExpenseCommandValidator().Validate(new CreateExpenseCommand(
            Guid.NewGuid(), Guid.NewGuid(), new DateOnly(2026, 1, 1), null, null, null, false, null,
            [new ExpenseLineInput(Guid.NewGuid(), 1m, VatRate.NoVat, VatCreditUse.Taxable, (VatCreditCategory)9)]));
        Assert.Contains(expense.Errors, x => x.PropertyName.EndsWith("CreditCategory", StringComparison.Ordinal));
        await Task.CompletedTask;
    }

    private static PurchaseBillLineInput Line(Guid productId, decimal amount, VatCreditUse use, VatCreditCategory category) =>
        new(productId, 1m, amount, VatRate.ThirteenPercentVat, ExpenditureClassification.Others, CreditUse: use, CreditCategory: category);

    private static decimal Net(IEnumerable<GlLine> lines, Guid accountId) =>
        lines.Where(x => x.AccountId == accountId).Sum(x => x.Debit - x.Credit);

    private static VatReturnLineDto Line(VatSummaryReportDto report, string key) => Assert.Single(report.Lines, x => x.Key == key);

    private static IReadOnlyList<decimal> Monthly(VatSummaryReportDto report, string key) => Line(report, key).Monthly;

    private static Task<VatSummaryReportDto> ReturnAsync(IAppDbContext db, Seed seed) =>
        new VatSummaryReportQueryHandler(db).Handle(new VatSummaryReportQuery(seed.OrganizationId, Fy), CancellationToken.None);

    private static async Task<List<GlLine>> GlLinesAsync(IAppDbContext db, DocumentType type, Guid id) =>
        await db.GlJournalEntries.Include(x => x.Lines)
            .Where(x => x.SourceDocumentType == type && x.SourceDocumentId == id)
            .SelectMany(x => x.Lines)
            .ToListAsync();

    private static async Task SetCategoryAsync(IAppDbContext db, Seed seed, Guid productId, VatCreditCategory category)
    {
        var product = await db.Products.SingleAsync(x => x.Id == productId);
        product.SetVatCreditCategory(category);
        await db.SaveChangesAsync(CancellationToken.None);
        Assert.Equal(seed.OrganizationId, product.OrganizationId);
    }

    private static async Task<Guid> CreateBillAsync(
        IAppDbContext db, Seed seed, DateOnly date, IReadOnlyList<PurchaseBillLineInput> lines)
    {
        var created = await new CreatePurchaseBillCommandHandler(db).Handle(
            new CreatePurchaseBillCommand(
                seed.OrganizationId, seed.SupplierId, seed.WarehouseId, date, null, null, false, null, null, null, null, lines),
            CancellationToken.None);
        return created.Id;
    }

    private static async Task<Guid> CreateAndApproveBillAsync(
        IAppDbContext db, Seed seed, DateOnly date, IReadOnlyList<PurchaseBillLineInput> lines)
    {
        var id = await CreateBillAsync(db, seed, date, lines);
        await new ApprovePurchaseBillCommandHandler(
            db, seed.NumberGenerator, new FakeCurrentUserService(Guid.NewGuid()), new PurchaseBillPostingRule(),
            new StockLedgerService(db))
            .Handle(new ApprovePurchaseBillCommand(seed.OrganizationId, id), CancellationToken.None);
        return id;
    }

    private static async Task<Guid> CreateAndApproveDebitNoteAsync(
        IAppDbContext db, Seed seed, DateOnly date, Guid billId, IReadOnlyList<DebitNoteLineInput> lines,
        Guid? warehouseId = null)
    {
        var created = await new CreateDebitNoteCommandHandler(db).Handle(
            new CreateDebitNoteCommand(
                seed.OrganizationId, seed.SupplierId, date, null, null, lines, DocumentType.PurchaseBill, billId)
            {
                WarehouseId = warehouseId,
            },
            CancellationToken.None);
        await new ApproveDebitNoteCommandHandler(
            db, seed.NumberGenerator, new FakeCurrentUserService(Guid.NewGuid()), new DebitNotePostingRule(),
            new StockLedgerService(db))
            .Handle(new ApproveDebitNoteCommand(seed.OrganizationId, created.Id), CancellationToken.None);
        return created.Id;
    }

    private static async Task<Guid> CreateAndApproveExpenseAsync(IAppDbContext db, Seed seed, DateOnly date, ExpenseLineInput line)
    {
        var created = await new CreateExpenseCommandHandler(db).Handle(
            new CreateExpenseCommand(seed.OrganizationId, seed.SupplierId, date, null, null, null, false, null, [line]),
            CancellationToken.None);
        await new ApproveExpenseCommandHandler(
            db, seed.NumberGenerator, new FakeCurrentUserService(Guid.NewGuid()), new ExpensePostingRule())
            .Handle(new ApproveExpenseCommand(seed.OrganizationId, created.Id), CancellationToken.None);
        return created.Id;
    }

    private static async Task CreateAndApproveInvoiceAsync(IAppDbContext db, Seed seed, DateOnly date, decimal amount, VatRate vatRate)
    {
        var created = await new CreateInvoiceCommandHandler(db, await SalesPricing.PricerAsync(db, seed.OrganizationId)).Handle(
            new CreateInvoiceCommand(
                seed.OrganizationId, seed.CustomerId, seed.WarehouseId, date, null,
                [new InvoiceLineInput(seed.ServiceId, 1m, amount, vatRate)]),
            CancellationToken.None);
        var stockLedgerService = new StockLedgerService(db);
        await new ApproveInvoiceCommandHandler(
            db, seed.NumberGenerator, new FakeCurrentUserService(Guid.NewGuid()), new InvoicePostingRule(),
            new FifoStockAvailabilityPolicy(db, stockLedgerService), stockLedgerService, new ContactCreditLimitPolicy(db))
            .Handle(new ApproveInvoiceCommand(seed.OrganizationId, created.Id, OverrideWarning: false), CancellationToken.None);
    }

    private sealed record Seed(
        Guid OrganizationId, FakeDocumentNumberGenerator NumberGenerator, Guid CustomerId, Guid SupplierId, Guid WarehouseId,
        Guid ServiceId, Guid GoodsId, Guid PurchaseAccountId, Guid VatReceivableId, Guid PayableId, Guid InventoryId,
        Guid InventoryAdjustmentId);

    private static async Task<Seed> SeedAsync(IAppDbContext db)
    {
        var organizationId = Guid.NewGuid();
        var numbers = new FakeDocumentNumberGenerator();

        var customer = await new CreateContactCommandHandler(db, numbers).Handle(
            new CreateContactCommand(organizationId, ContactType.Customer, "Acme Traders", null, null, null, null, null, 0m),
            CancellationToken.None);
        var supplier = await new CreateContactCommandHandler(db, numbers).Handle(
            new CreateContactCommand(organizationId, ContactType.Supplier, "Global Supplies", null, null, null, null, null, 0m),
            CancellationToken.None);
        var warehouse = await new CreateWarehouseCommandHandler(db).Handle(
            new CreateWarehouseCommand(organizationId, "Main Warehouse"), CancellationToken.None);
        var category = await new CreateProductCategoryCommandHandler(db).Handle(
            new CreateProductCategoryCommand(organizationId, "General", null), CancellationToken.None);
        var unit = await new CreateUnitOfMeasurementCommandHandler(db).Handle(
            new CreateUnitOfMeasurementCommand(organizationId, "Piece", "pc"), CancellationToken.None);
        var service = await new CreateProductCommandHandler(db, numbers).Handle(
            new CreateProductCommand(
                organizationId, ProductType.Service, "Consulting", category.Id, unit.Id, null, true, 100m, 80m,
                VatRate.ThirteenPercentVat, 0, false),
            CancellationToken.None);
        var goods = await new CreateProductCommandHandler(db, numbers).Handle(
            new CreateProductCommand(
                organizationId, ProductType.Goods, "Delivery van", category.Id, unit.Id, null, true, 100m, 80m,
                VatRate.ThirteenPercentVat, 0, true),
            CancellationToken.None);

        var assets = await new CreateAccountGroupCommandHandler(db).Handle(
            new CreateAccountGroupCommand(organizationId, "Current Assets", AccountRootType.Asset, null), CancellationToken.None);
        var liabilities = await new CreateAccountGroupCommandHandler(db).Handle(
            new CreateAccountGroupCommand(organizationId, "Current Liabilities", AccountRootType.Liability, null), CancellationToken.None);
        var income = await new CreateAccountGroupCommandHandler(db).Handle(
            new CreateAccountGroupCommand(organizationId, "Sales Income", AccountRootType.Income, null), CancellationToken.None);
        var expenses = await new CreateAccountGroupCommandHandler(db).Handle(
            new CreateAccountGroupCommand(organizationId, "Operating Expenses", AccountRootType.Expense, null), CancellationToken.None);

        async Task<Guid> AccountAsync(string name, Guid groupId) =>
            (await new CreateAccountCommandHandler(db, numbers).Handle(
                new CreateAccountCommand(organizationId, name, groupId), CancellationToken.None)).Id;

        var receivable = await AccountAsync("Accounts Receivable", assets.Id);
        var vatPayable = await AccountAsync("VAT Payable", liabilities.Id);
        var sales = await AccountAsync("Sales Revenue", income.Id);
        var vatReceivable = await AccountAsync("VAT Receivable", assets.Id);
        var payable = await AccountAsync("Accounts Payable", liabilities.Id);
        var purchase = await AccountAsync("Purchase Expense", expenses.Id);
        var inventory = await AccountAsync("Inventory", assets.Id);
        var cogs = await AccountAsync("Cost of Goods Sold", expenses.Id);
        var adjustment = await AccountAsync("Inventory Adjustment", expenses.Id);

        var settings = TenantSettings.CreateDefault(organizationId);
        settings.SetAccountingDefaults(sales, receivable, vatPayable, purchase, payable, vatReceivable, null);
        settings.SetInventoryDefaults(inventory, cogs, adjustment, null);
        db.TenantSettings.Add(settings);
        await db.SaveChangesAsync(CancellationToken.None);

        return new Seed(
            organizationId, numbers, customer.Id, supplier.Id, warehouse.Id, service.Id, goods.Id, purchase, vatReceivable,
            payable, inventory, adjustment);
    }
}
