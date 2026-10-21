using ErpApp.Application.Accounting.Cash;
using ErpApp.Application.Common.Persistence;
using ErpApp.Application.Payments;
using ErpApp.Application.Payments.Commands.ApplyPaymentAllocation;
using ErpApp.Application.Payments.Commands.ApprovePayment;
using ErpApp.Application.Payments.Commands.CreatePayment;
using ErpApp.Application.Payments.Posting;
using ErpApp.Application.Pos.Commands.CreatePosRefund;
using ErpApp.Application.Inventory.Stock;
using ErpApp.Application.Sales;
using ErpApp.Application.Sales.Commands.ApproveCreditNote;
using ErpApp.Application.Sales.Commands.CreateCreditNote;
using ErpApp.Application.Sales.Posting;
using ErpApp.Application.UnitTests.Pos;
using ErpApp.Application.UnitTests.TestSupport;
using ErpApp.Domain.Accounting;
using ErpApp.Domain.Catalog;
using ErpApp.Domain.Common;
using ErpApp.Domain.Inventory;
using ErpApp.Domain.Payments;
using ErpApp.Domain.Purchasing;
using ErpApp.Domain.Sales;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.UnitTests.Accounting;

/// <summary>
/// Phase 87 -- an invoice and a purchase bill record every account their approval posts to, and what later reverses or
/// settles them uses the same ones: a credit note (return or price adjustment), a debit note, a till refund's payout, and
/// a payment allocated to them. Each test switches every tenant default between the original and what follows it, and
/// holds the result to the twin that happened before the switch.
/// </summary>
public sealed class RecordedPostingAccountsTests
{
    private static readonly DateOnly Day = new(2026, 10, 12);

    private sealed record Defaults(
        Guid Sales, Guid Receivable, Guid VatPayable, Guid Purchase, Guid Payable, Guid VatReceivable, Guid Inventory,
        Guid Cogs, Guid Adjustment);

    private static async Task<Defaults> CurrentAsync(IAppDbContext db, Guid organizationId)
    {
        var s = await db.TenantSettings.SingleAsync(x => x.OrganizationId == organizationId);
        return new Defaults(
            s.DefaultSalesAccountId!.Value, s.DefaultAccountsReceivableId!.Value, s.DefaultVatPayableAccountId!.Value,
            s.DefaultPurchaseAccountId!.Value, s.DefaultAccountsPayableId!.Value, s.DefaultVatReceivableAccountId!.Value,
            s.DefaultInventoryAccountId!.Value, s.DefaultCogsAccountId!.Value, s.DefaultInventoryAdjustmentAccountId!.Value);
    }

    /// <summary>Every default moved to a new account of the same kind.</summary>
    private static async Task<Defaults> SwitchEveryDefaultAsync(IAppDbContext db, Guid organizationId)
    {
        Guid Add(string name, AccountRootType root)
        {
            var account = Account.Create(organizationId, "N" + Guid.NewGuid().ToString("N")[..5], name, root, Guid.NewGuid());
            db.Accounts.Add(account);
            return account.Id;
        }

        var next = new Defaults(
            Add("Sales 2083", AccountRootType.Income), Add("Receivable 2083", AccountRootType.Asset),
            Add("VAT Payable 2083", AccountRootType.Liability), Add("Purchase 2083", AccountRootType.Expense),
            Add("Payable 2083", AccountRootType.Liability), Add("VAT Receivable 2083", AccountRootType.Asset),
            Add("Inventory 2083", AccountRootType.Asset), Add("COGS 2083", AccountRootType.Expense),
            Add("Adjustment 2083", AccountRootType.Expense));

        var settings = await db.TenantSettings.SingleAsync(x => x.OrganizationId == organizationId);
        settings.SetAccountingDefaults(
            next.Sales, next.Receivable, next.VatPayable, next.Purchase, next.Payable, next.VatReceivable, null);
        settings.SetInventoryDefaults(next.Inventory, next.Cogs, next.Adjustment, null);
        await db.SaveChangesAsync();
        return next;
    }

    private static async Task<decimal> BalanceAsync(IAppDbContext db, Guid organizationId, Guid accountId)
    {
        var rows = await (
            from line in db.GlLines
            join entry in db.GlJournalEntries on line.GlJournalEntryId equals entry.Id
            where entry.OrganizationId == organizationId && line.AccountId == accountId
            select new { line.Debit, line.Credit }).ToListAsync();
        return rows.Sum(x => x.Debit - x.Credit);
    }

    /// <summary>What a document posted, net per account, every entry it has.</summary>
    private static async Task<Dictionary<Guid, decimal>> PostedAsync(IAppDbContext db, DocumentType type, Guid id)
    {
        var rows = await (
            from line in db.GlLines
            join entry in db.GlJournalEntries on line.GlJournalEntryId equals entry.Id
            where entry.SourceDocumentType == type && entry.SourceDocumentId == id
            select new { line.AccountId, line.Debit, line.Credit }).ToListAsync();
        return rows.GroupBy(x => x.AccountId).ToDictionary(g => g.Key, g => g.Sum(x => x.Debit - x.Credit));
    }

    private static async Task AssertUntouchedAsync(IAppDbContext db, Guid organizationId, Defaults next)
    {
        foreach (var account in new[]
                 {
                     next.Sales, next.Receivable, next.VatPayable, next.Purchase, next.Payable, next.VatReceivable,
                     next.Inventory, next.Cogs,
                 })
        {
            Assert.Equal(0m, await BalanceAsync(db, organizationId, account));
        }
    }

    // ---- Sales ---------------------------------------------------------------------------------------

    [Fact]
    public async Task An_invoice_records_every_account_its_approval_posted_to()
    {
        var db = TestAppDbContext.Create();
        var seed = await InventoryReportSeed.CreateAsync(db);
        var old = await CurrentAsync(db, seed.OrganizationId);
        await InventoryReportSeed.PurchaseAsync(db, seed, Day, 10m, 100m, vatRate: VatRate.ThirteenPercentVat);

        var sale = await InventoryReportSeed.SellAsync(db, seed, Day, 4m, 150m, vatRate: VatRate.ThirteenPercentVat);

        var invoice = await db.Invoices.Include(x => x.Lines).SingleAsync(x => x.Id == sale.Id);
        Assert.Equal(
            (old.Receivable, old.VatPayable, old.Cogs, old.Inventory),
            (invoice.ReceivableAccountId!.Value, invoice.VatPayableAccountId!.Value, invoice.CogsAccountId!.Value,
                invoice.InventoryAccountId!.Value));
        Assert.Equal(old.Sales, Assert.Single(invoice.Lines).SalesAccountId);

        var bill = await db.PurchaseBills.Include(x => x.Lines).SingleAsync();
        Assert.Equal(
            (old.Payable, old.VatReceivable, old.Inventory),
            (bill.PayableAccountId!.Value, bill.VatReceivableAccountId!.Value, bill.InventoryAccountId!.Value));
        Assert.Null(bill.TdsPayableAccountId);
        Assert.Equal(old.Inventory, Assert.Single(bill.Lines).DebitAccountId);
    }

    [Fact]
    public async Task A_return_after_every_default_changed_reverses_into_the_accounts_its_invoice_used()
    {
        var db = TestAppDbContext.Create();
        var seed = await InventoryReportSeed.CreateAsync(db);
        await InventoryReportSeed.PurchaseAsync(db, seed, Day, 10m, 100m);

        // Two identical sales; the twin's return is made before the switch, the other's after it.
        var away = await InventoryReportSeed.SellAsync(db, seed, Day, 4m, 150m, vatRate: VatRate.ThirteenPercentVat);
        var twin = await InventoryReportSeed.SellAsync(db, seed, Day, 4m, 150m, vatRate: VatRate.ThirteenPercentVat);
        var before = await InventoryReportSeed.CreditNoteAsync(db, seed, Day, 2m, 150m, twin.Id, vatRate: VatRate.ThirteenPercentVat);

        var next = await SwitchEveryDefaultAsync(db, seed.OrganizationId);
        var after = await InventoryReportSeed.CreditNoteAsync(db, seed, Day, 2m, 150m, away.Id, vatRate: VatRate.ThirteenPercentVat);

        Assert.Equal(
            await PostedAsync(db, DocumentType.CreditNote, before.Id),
            await PostedAsync(db, DocumentType.CreditNote, after.Id));
        await AssertUntouchedAsync(db, seed.OrganizationId, next);
    }

    [Fact]
    public async Task A_price_adjustment_follows_its_invoice_and_a_product_the_invoice_never_had_takes_todays_account()
    {
        var db = TestAppDbContext.Create();
        var seed = await InventoryReportSeed.CreateAsync(db);
        var old = await CurrentAsync(db, seed.OrganizationId);
        await InventoryReportSeed.PurchaseAsync(db, seed, Day, 10m, 100m);
        var sale = await InventoryReportSeed.SellAsync(db, seed, Day, 4m, 150m, vatRate: VatRate.ThirteenPercentVat);
        var next = await SwitchEveryDefaultAsync(db, seed.OrganizationId);

        // An ERP note against the picked invoice: 50 off the Widget it sold, 10 off a Gadget it never had.
        var created = await new CreateCreditNoteCommandHandler(db).Handle(
            new CreateCreditNoteCommand(
                seed.OrganizationId, seed.CustomerId, Day, null,
                [
                    new CreditNoteLineInput(seed.ProductId, 1m, 50m, VatRate.ThirteenPercentVat),
                    new CreditNoteLineInput(seed.SecondProductId, 1m, 10m, VatRate.NoVat),
                ],
                null, null)
            { AgainstInvoiceId = sale.Id, Reason = "Damaged in transit" },
            CancellationToken.None);
        await new ApproveCreditNoteCommandHandler(
                db, seed.NumberGenerator, new FakeCurrentUserService(Guid.NewGuid()), new CreditNotePostingRule(),
                new StockLedgerService(db))
            .Handle(new ApproveCreditNoteCommand(seed.OrganizationId, created.Id), CancellationToken.None);

        var posted = await PostedAsync(db, DocumentType.CreditNote, created.Id);
        Assert.Equal(50m, posted[old.Sales]);
        Assert.Equal(6.50m, posted[old.VatPayable]);
        Assert.Equal(-66.50m, posted[old.Receivable]);
        Assert.Equal(10m, posted[next.Sales]);
        Assert.False(posted.ContainsKey(next.Receivable));
        Assert.False(posted.ContainsKey(next.VatPayable));
    }

    [Fact]
    public async Task A_sale_from_before_phase_87_reverses_through_todays_settings()
    {
        var db = TestAppDbContext.Create();
        var seed = await InventoryReportSeed.CreateAsync(db);
        await InventoryReportSeed.PurchaseAsync(db, seed, Day, 10m, 100m);
        var sale = await InventoryReportSeed.SellAsync(db, seed, Day, 4m, 150m, vatRate: VatRate.ThirteenPercentVat);

        // Nothing recorded, as on every sale approved before this phase.
        var invoice = await db.Invoices.Include(x => x.Lines).SingleAsync(x => x.Id == sale.Id);
        var entry = ((DbContext)db).Entry(invoice);
        foreach (var name in new[]
                 {
                     nameof(Invoice.ReceivableAccountId), nameof(Invoice.VatPayableAccountId), nameof(Invoice.CogsAccountId),
                     nameof(Invoice.InventoryAccountId),
                 })
        {
            entry.Property(name).CurrentValue = null;
        }

        ((DbContext)db).Entry(invoice.Lines[0]).Property(nameof(InvoiceLine.SalesAccountId)).CurrentValue = null;
        await db.SaveChangesAsync();

        var next = await SwitchEveryDefaultAsync(db, seed.OrganizationId);
        var note = await InventoryReportSeed.CreditNoteAsync(db, seed, Day, 2m, 150m, sale.Id, vatRate: VatRate.ThirteenPercentVat);

        var posted = await PostedAsync(db, DocumentType.CreditNote, note.Id);
        Assert.Equal(300m, posted[next.Sales]);
        Assert.Equal(39m, posted[next.VatPayable]);
        Assert.Equal(-339m, posted[next.Receivable]);
        Assert.Equal(200m, posted[next.Inventory]);
        Assert.Equal(-200m, posted[next.Cogs]);
    }

    // ---- Purchases ---------------------------------------------------------------------------------------

    [Fact]
    public async Task A_debit_note_after_every_default_changed_reverses_into_the_accounts_its_bill_used()
    {
        var db = TestAppDbContext.Create();
        var seed = await InventoryReportSeed.CreateAsync(db);
        var away = await InventoryReportSeed.PurchaseAsync(db, seed, Day, 10m, 100m, vatRate: VatRate.ThirteenPercentVat);
        var twin = await InventoryReportSeed.PurchaseAsync(db, seed, Day, 10m, 100m, vatRate: VatRate.ThirteenPercentVat);
        var before = await InventoryReportSeed.DebitNoteAsync(db, seed, Day, 3m, 100m, twin.Id, vatRate: VatRate.ThirteenPercentVat);

        var next = await SwitchEveryDefaultAsync(db, seed.OrganizationId);
        var after = await InventoryReportSeed.DebitNoteAsync(db, seed, Day, 3m, 100m, away.Id, vatRate: VatRate.ThirteenPercentVat);

        Assert.Equal(
            await PostedAsync(db, DocumentType.DebitNote, before.Id),
            await PostedAsync(db, DocumentType.DebitNote, after.Id));
        await AssertUntouchedAsync(db, seed.OrganizationId, next);
    }

    [Fact]
    public async Task A_returns_price_to_cost_difference_goes_to_the_adjustment_account_its_bill_recorded()
    {
        // Phase 95 -- phase 87's Decision G reopened by the user (2026-10-20): the difference between a return's price
        // (the bill's 100) and the cost the stock gives up (the oldest layers, 80) follows the bill too, not today's
        // Inventory Adjustment default.
        var db = TestAppDbContext.Create();
        var seed = await InventoryReportSeed.CreateAsync(db);
        var original = (await CurrentAsync(db, seed.OrganizationId)).Adjustment;
        await InventoryReportSeed.PurchaseAsync(db, seed, Day, 10m, 80m, vatRate: VatRate.ThirteenPercentVat);
        var away = await InventoryReportSeed.PurchaseAsync(db, seed, Day, 10m, 100m, vatRate: VatRate.ThirteenPercentVat);
        var twin = await InventoryReportSeed.PurchaseAsync(db, seed, Day, 10m, 100m, vatRate: VatRate.ThirteenPercentVat);
        var before = await InventoryReportSeed.DebitNoteAsync(db, seed, Day, 3m, 100m, twin.Id, vatRate: VatRate.ThirteenPercentVat);
        Assert.Equal(-60m, (await PostedAsync(db, DocumentType.DebitNote, before.Id))[original]);

        var next = await SwitchEveryDefaultAsync(db, seed.OrganizationId);
        var after = await InventoryReportSeed.DebitNoteAsync(db, seed, Day, 3m, 100m, away.Id, vatRate: VatRate.ThirteenPercentVat);

        Assert.Equal(
            await PostedAsync(db, DocumentType.DebitNote, before.Id),
            await PostedAsync(db, DocumentType.DebitNote, after.Id));
        await AssertUntouchedAsync(db, seed.OrganizationId, next);
        Assert.Equal(0m, await BalanceAsync(db, seed.OrganizationId, next.Adjustment));
    }

    // ---- Payments ----------------------------------------------------------------------------------------

    private static async Task<Guid> CashAccountAsync(IAppDbContext db, Guid organizationId)
    {
        var cash = Account.Create(organizationId, "1010", "Cash In Hand", AccountRootType.Asset, Guid.NewGuid(), AccountKind.Cash);
        db.Accounts.Add(cash);
        await db.SaveChangesAsync();
        return cash.Id;
    }

    private static async Task<Guid> PayAsync(
        IAppDbContext db, InventoryReportSeed.Seed seed, PaymentDirection direction, Guid cash, decimal amount,
        params PaymentAllocationInput[] allocations)
    {
        var created = await new CreatePaymentCommandHandler(db).Handle(
            new CreatePaymentCommand(
                seed.OrganizationId, direction == PaymentDirection.Received ? seed.CustomerId : seed.SupplierId, direction,
                Day, null, cash, amount, null, allocations),
            CancellationToken.None);
        await new ApprovePaymentCommandHandler(
                db, seed.NumberGenerator, new FakeCurrentUserService(Guid.NewGuid()), new PaymentPostingRule(),
                new GlCashBalancePolicy(db))
            .Handle(new ApprovePaymentCommand(seed.OrganizationId, created.Id, OverrideNegativeCashBalanceWarning: true),
                CancellationToken.None);
        return created.Id;
    }

    [Fact]
    public async Task A_receipt_settles_each_invoice_out_of_the_receivable_it_recorded()
    {
        var db = TestAppDbContext.Create();
        var seed = await InventoryReportSeed.CreateAsync(db);
        var old = await CurrentAsync(db, seed.OrganizationId);
        var cash = await CashAccountAsync(db, seed.OrganizationId);
        await InventoryReportSeed.PurchaseAsync(db, seed, Day, 10m, 100m);

        // 4 x 150 + 13% = 678 on the old Receivable; the same again after the switch, on the new one.
        var x = await InventoryReportSeed.SellAsync(db, seed, Day, 4m, 150m, vatRate: VatRate.ThirteenPercentVat);
        var next = await SwitchEveryDefaultAsync(db, seed.OrganizationId);
        var y = await InventoryReportSeed.SellAsync(db, seed, Day, 4m, 150m, vatRate: VatRate.ThirteenPercentVat);

        // 1000 received: all of X, 100 of Y, 222 left on account.
        var receipt = await PayAsync(
            db, seed, PaymentDirection.Received, cash, 1000m,
            new PaymentAllocationInput(DocumentType.Invoice, x.Id, 678m),
            new PaymentAllocationInput(DocumentType.Invoice, y.Id, 100m));

        var posted = await PostedAsync(db, DocumentType.Payment, receipt);
        Assert.Equal(1000m, posted[cash]);
        Assert.Equal(-678m, posted[old.Receivable]);
        Assert.Equal(-322m, posted[next.Receivable]);
        Assert.Equal(0m, await BalanceAsync(db, seed.OrganizationId, old.Receivable));
        Assert.Equal(678m - 322m, await BalanceAsync(db, seed.OrganizationId, next.Receivable));
        Assert.Equal(next.Receivable, (await db.Payments.SingleAsync(p => p.Id == receipt)).UnallocatedControlAccountId);
    }

    [Fact]
    public async Task A_receipt_whose_invoices_all_sit_on_todays_receivable_posts_the_one_line_entry_it_always_did()
    {
        var db = TestAppDbContext.Create();
        var seed = await InventoryReportSeed.CreateAsync(db);
        var old = await CurrentAsync(db, seed.OrganizationId);
        var cash = await CashAccountAsync(db, seed.OrganizationId);
        await InventoryReportSeed.PurchaseAsync(db, seed, Day, 10m, 100m);
        var x = await InventoryReportSeed.SellAsync(db, seed, Day, 4m, 150m);

        var receipt = await PayAsync(
            db, seed, PaymentDirection.Received, cash, 1000m, new PaymentAllocationInput(DocumentType.Invoice, x.Id, 600m));

        var entry = await db.GlJournalEntries.Include(e => e.Lines)
            .SingleAsync(e => e.SourceDocumentType == DocumentType.Payment && e.SourceDocumentId == receipt);
        Assert.Equal(
            [(cash, 1000m, 0m), (old.Receivable, 0m, 1000m)],
            entry.Lines.Select(l => (l.AccountId, l.Debit, l.Credit)).ToList());
    }

    [Fact]
    public async Task A_later_allocation_moves_its_share_to_the_receivable_its_invoice_recorded()
    {
        var db = TestAppDbContext.Create();
        var seed = await InventoryReportSeed.CreateAsync(db);
        var old = await CurrentAsync(db, seed.OrganizationId);
        var cash = await CashAccountAsync(db, seed.OrganizationId);
        await InventoryReportSeed.PurchaseAsync(db, seed, Day, 10m, 100m);
        var x = await InventoryReportSeed.SellAsync(db, seed, Day, 4m, 150m, vatRate: VatRate.ThirteenPercentVat);
        var next = await SwitchEveryDefaultAsync(db, seed.OrganizationId);

        // 500 on account, on today's Receivable; then 300 of it applied to X, which sits on the old one.
        var receipt = await PayAsync(db, seed, PaymentDirection.Received, cash, 500m);
        Assert.Equal(-500m, await BalanceAsync(db, seed.OrganizationId, next.Receivable));

        await new ApplyPaymentAllocationCommandHandler(db).Handle(
            new ApplyPaymentAllocationCommand(seed.OrganizationId, DocumentType.Payment, receipt, null, DocumentType.Invoice, x.Id, 300m),
            CancellationToken.None);

        Assert.Equal(678m - 300m, await BalanceAsync(db, seed.OrganizationId, old.Receivable));
        Assert.Equal(-200m, await BalanceAsync(db, seed.OrganizationId, next.Receivable));
        Assert.Equal(500m, await BalanceAsync(db, seed.OrganizationId, cash));
    }

    [Fact]
    public async Task A_supplier_payment_settles_each_bill_out_of_the_payable_it_recorded()
    {
        var db = TestAppDbContext.Create();
        var seed = await InventoryReportSeed.CreateAsync(db);
        var old = await CurrentAsync(db, seed.OrganizationId);
        var cash = await CashAccountAsync(db, seed.OrganizationId);
        var bill = await InventoryReportSeed.PurchaseAsync(db, seed, Day, 10m, 100m);
        var next = await SwitchEveryDefaultAsync(db, seed.OrganizationId);
        // Cash to pay with: a customer's advance, on the receivable side, which the payable figures below never touch.
        await PayAsync(db, seed, PaymentDirection.Received, cash, 2000m);

        var payment = await PayAsync(
            db, seed, PaymentDirection.Paid, cash, 1200m, new PaymentAllocationInput(DocumentType.PurchaseBill, bill.Id, 1000m));

        var posted = await PostedAsync(db, DocumentType.Payment, payment);
        Assert.Equal(-1200m, posted[cash]);
        Assert.Equal(1000m, posted[old.Payable]);
        Assert.Equal(200m, posted[next.Payable]);
        Assert.Equal(0m, await BalanceAsync(db, seed.OrganizationId, old.Payable));
    }

    // ---- The till ----------------------------------------------------------------------------------------

    [Fact]
    public async Task A_till_refunds_payout_debits_the_receivable_its_sale_recorded()
    {
        var home = await PosTestTill.CreateAsync();
        var session = await home.OpenAsync();
        var sale = await home.SellAsync(session.Id, [home.Coke(2)], [home.Cash(136m)]);
        var oldReceivable = await home.ReceivableAccountIdAsync();
        var next = await SwitchEveryDefaultAsync(home.Db, home.OrganizationId);

        var line = await home.ReturnAsync(sale.Id, home.CokeId, 1m);
        var preview = await home.PreviewRefundAsync(session.Id, sale.Id, [line]);
        await home.RefundAsync(session.Id, sale.Id, [line], [home.Cash(preview.RequiredPayout)]);

        // The note credits the sale's Receivable and its payout debits the same one: nothing lands on the new account.
        Assert.Equal(0m, await home.BalanceAsync(oldReceivable));
        Assert.Equal(0m, await home.BalanceAsync(next.Receivable));
        Assert.Equal(0m, await home.BalanceAsync(next.Sales));
    }
}
