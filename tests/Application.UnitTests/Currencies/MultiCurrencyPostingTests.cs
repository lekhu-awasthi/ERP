using ErpApp.Application.Accounting.Cash;
using ErpApp.Application.Accounting;
using ErpApp.Application.Accounting.Commands.ApproveJournalVoucher;
using ErpApp.Application.Accounting.Commands.CreateAccount;
using ErpApp.Application.Accounting.Commands.CreateAccountGroup;
using ErpApp.Application.Accounting.Commands.CreateJournalVoucher;
using ErpApp.Application.Accounting.Posting;
using ErpApp.Application.Catalog.Commands.CreateProduct;
using ErpApp.Application.Catalog.Commands.CreateProductCategory;
using ErpApp.Application.Catalog.Commands.CreateUnitOfMeasurement;
using ErpApp.Application.Common.Exceptions;
using ErpApp.Application.Common.Persistence;
using ErpApp.Application.Contacts.Commands.CreateContact;
using ErpApp.Application.Contacts.Queries.ContactStatement;
using ErpApp.Application.Contacts.Queries.DocumentAge;
using ErpApp.Application.Inventory.Stock;
using ErpApp.Application.Payments;
using ErpApp.Application.Payments.Commands.ApplyPaymentAllocation;
using ErpApp.Application.Payments.Commands.ApprovePayment;
using ErpApp.Application.Payments.Commands.CreatePayment;
using ErpApp.Application.Payments.Commands.VoidPayment;
using ErpApp.Application.Payments.Posting;
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
using ErpApp.Domain.Payments;
using ErpApp.Domain.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.UnitTests.Currencies;

/// <summary>
/// Phase 28's central claim, driven end to end through the real handlers: <b>a document stores its
/// amounts in its own currency, and the general ledger it posts is in the base currency</b> -- plus
/// the realised forex rule that keeps the control account flat when a settlement rate differs from
/// a booking rate.
/// </summary>
public class MultiCurrencyPostingTests
{
    private const decimal UsdRate = 133m;

    [Fact]
    public async Task An_invoice_stores_its_own_currency_and_posts_the_ledger_in_base_currency()
    {
        var db = TestAppDbContext.Create();
        var seed = await SeedAsync(db);

        var invoiceId = await CreateAndApproveInvoiceAsync(db, seed, amount: 100m, "USD", UsdRate);

        var invoice = await db.Invoices.Include(x => x.Lines).SingleAsync(x => x.Id == invoiceId);
        Assert.Equal("USD", invoice.CurrencyCode);
        Assert.Equal(UsdRate, invoice.ExchangeRate);

        // Stored in the transaction currency -- the fold never touches the document itself.
        Assert.Equal(100m, Assert.Single(invoice.Lines).Amount);

        var entry = await db.GlJournalEntries.Include(x => x.Lines)
            .SingleAsync(x => x.SourceDocumentType == DocumentType.Invoice && x.SourceDocumentId == invoiceId);

        // Posted in the base currency: 100 USD x 133 = 13,300 NPR, on both sides.
        Assert.Equal(13300m, entry.Lines.Sum(x => x.Debit));
        Assert.Equal(13300m, entry.Lines.Sum(x => x.Credit));
        Assert.Equal(13300m, entry.Lines.Single(x => x.AccountId == seed.ArAccountId).Debit);
    }

    [Fact]
    public async Task A_base_currency_invoice_posts_exactly_what_it_always_did()
    {
        var db = TestAppDbContext.Create();
        var seed = await SeedAsync(db);

        var invoiceId = await CreateAndApproveInvoiceAsync(db, seed, amount: 1000m, currencyCode: null, exchangeRate: null);

        var entry = await db.GlJournalEntries.Include(x => x.Lines)
            .SingleAsync(x => x.SourceDocumentType == DocumentType.Invoice && x.SourceDocumentId == invoiceId);

        Assert.Equal(1000m, entry.Lines.Sum(x => x.Debit));
        Assert.Equal(1000m, entry.Lines.Sum(x => x.Credit));
    }

    [Fact]
    public async Task A_receipt_settling_at_a_worse_rate_books_a_forex_loss()
    {
        // Booked:  100 USD at 133 = 13,300 debited to AR.
        // Settled: 100 USD at 130 = 13,000 received.
        // 300 fewer rupees arrived than were booked -- a loss, and AR must be cleared of the 300.
        var db = TestAppDbContext.Create();
        var seed = await SeedAsync(db);
        var invoiceId = await CreateAndApproveInvoiceAsync(db, seed, 100m, "USD", UsdRate);

        var paymentId = await CreateAndApprovePaymentAsync(db, seed, invoiceId, 100m, "USD", 130m);

        var entry = await db.GlJournalEntries.Include(x => x.Lines)
            .SingleAsync(x => x.SourceDocumentType == DocumentType.Payment && x.SourceDocumentId == paymentId);

        Assert.Equal(entry.Lines.Sum(x => x.Debit), entry.Lines.Sum(x => x.Credit));
        Assert.Equal(300m, entry.Lines.Where(x => x.AccountId == seed.ForexLossAccountId).Sum(x => x.Debit));
        Assert.Equal(0m, entry.Lines.Where(x => x.AccountId == seed.ForexGainAccountId).Sum(x => x.Debit + x.Credit));

        // The point of the whole rule: AR is left flat, not carrying an unsettleable 300.
        Assert.Equal(0m, await ArNetMovementAsync(db, seed));
    }

    [Fact]
    public async Task A_receipt_settling_at_a_better_rate_books_a_forex_gain()
    {
        // Booked at 133, settled at 136 -- 300 more rupees arrived than were booked.
        var db = TestAppDbContext.Create();
        var seed = await SeedAsync(db);
        var invoiceId = await CreateAndApproveInvoiceAsync(db, seed, 100m, "USD", UsdRate);

        var paymentId = await CreateAndApprovePaymentAsync(db, seed, invoiceId, 100m, "USD", 136m);

        var entry = await db.GlJournalEntries.Include(x => x.Lines)
            .SingleAsync(x => x.SourceDocumentType == DocumentType.Payment && x.SourceDocumentId == paymentId);

        Assert.Equal(entry.Lines.Sum(x => x.Debit), entry.Lines.Sum(x => x.Credit));
        Assert.Equal(300m, entry.Lines.Where(x => x.AccountId == seed.ForexGainAccountId).Sum(x => x.Credit));
        Assert.Equal(0m, await ArNetMovementAsync(db, seed));
    }

    [Fact]
    public async Task A_receipt_settling_at_the_booking_rate_books_no_forex_leg_at_all()
    {
        var db = TestAppDbContext.Create();
        var seed = await SeedAsync(db);
        var invoiceId = await CreateAndApproveInvoiceAsync(db, seed, 100m, "USD", UsdRate);

        var paymentId = await CreateAndApprovePaymentAsync(db, seed, invoiceId, 100m, "USD", UsdRate);

        var entry = await db.GlJournalEntries.Include(x => x.Lines)
            .SingleAsync(x => x.SourceDocumentType == DocumentType.Payment && x.SourceDocumentId == paymentId);

        Assert.Equal(2, entry.Lines.Count);
        Assert.Equal(0m, await ArNetMovementAsync(db, seed));
    }

    [Fact]
    public async Task A_payment_cannot_be_allocated_to_a_document_in_another_currency()
    {
        var db = TestAppDbContext.Create();
        var seed = await SeedAsync(db);
        var invoiceId = await CreateAndApproveInvoiceAsync(db, seed, 100m, "USD", UsdRate);

        var ex = await Assert.ThrowsAsync<ConflictException>(
            () => CreateAndApprovePaymentAsync(db, seed, invoiceId, 100m, currencyCode: null, exchangeRate: null));

        Assert.Contains("its own currency", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_forex_difference_with_no_account_configured_fails_with_a_clear_message()
    {
        var db = TestAppDbContext.Create();
        var seed = await SeedAsync(db, configureForexAccounts: false);
        var invoiceId = await CreateAndApproveInvoiceAsync(db, seed, 100m, "USD", UsdRate);

        var ex = await Assert.ThrowsAsync<ConflictException>(
            () => CreateAndApprovePaymentAsync(db, seed, invoiceId, 100m, "USD", 130m));

        Assert.Contains("Forex Loss", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_foreign_currency_journal_voucher_books_its_conversion_residue_to_forex()
    {
        // Two 0.05 debits against one 0.10 credit, at rate 1.5:
        //   debits  0.075 -> 0.08 each = 0.16
        //   credit  0.150 -> 0.15
        // The 0.01 gap is a real conversion residue and is booked rather than absorbed. This is the
        // case that proves converting a *finished* GlLineInput list needs a residue leg at all --
        // and why every other document type converts its inputs before the rule instead.
        var db = TestAppDbContext.Create();
        var seed = await SeedAsync(db);

        var created = await new CreateJournalVoucherCommandHandler(db).Handle(
            new CreateJournalVoucherCommand(
                seed.OrganizationId, new DateOnly(2026, 1, 1), null,
                [
                    new JournalVoucherLineInput(seed.CashAccountId, 0.05m, 0m),
                    new JournalVoucherLineInput(seed.ArAccountId, 0.05m, 0m),
                    new JournalVoucherLineInput(seed.SalesAccountId, 0m, 0.10m),
                ])
            { CurrencyCode = "USD", ExchangeRate = 1.5m },
            CancellationToken.None);

        await new ApproveJournalVoucherCommandHandler(
            db, seed.NumberGenerator, new FakeCurrentUserService(Guid.NewGuid()), new JournalVoucherPostingRule(), new GlCashBalancePolicy(db))
            .Handle(new ApproveJournalVoucherCommand(seed.OrganizationId, created.Id), CancellationToken.None);

        var entry = await db.GlJournalEntries.Include(x => x.Lines)
            .SingleAsync(x => x.SourceDocumentType == DocumentType.JournalVoucher && x.SourceDocumentId == created.Id);

        Assert.Equal(entry.Lines.Sum(x => x.Debit), entry.Lines.Sum(x => x.Credit));
        Assert.Equal(0.16m, entry.Lines.Sum(x => x.Debit));
        Assert.Equal(0.01m, entry.Lines.Single(x => x.AccountId == seed.ForexGainAccountId).Credit);
    }

    [Fact]
    public async Task A_base_currency_journal_voucher_needs_no_forex_account()
    {
        var db = TestAppDbContext.Create();
        var seed = await SeedAsync(db, configureForexAccounts: false);

        var created = await new CreateJournalVoucherCommandHandler(db).Handle(
            new CreateJournalVoucherCommand(
                seed.OrganizationId, new DateOnly(2026, 1, 1), null,
                [
                    new JournalVoucherLineInput(seed.CashAccountId, 0.05m, 0m),
                    new JournalVoucherLineInput(seed.ArAccountId, 0.05m, 0m),
                    new JournalVoucherLineInput(seed.SalesAccountId, 0m, 0.10m),
                ]),
            CancellationToken.None);

        await new ApproveJournalVoucherCommandHandler(
            db, seed.NumberGenerator, new FakeCurrentUserService(Guid.NewGuid()), new JournalVoucherPostingRule(), new GlCashBalancePolicy(db))
            .Handle(new ApproveJournalVoucherCommand(seed.OrganizationId, created.Id), CancellationToken.None);

        var entry = await db.GlJournalEntries.Include(x => x.Lines)
            .SingleAsync(x => x.SourceDocumentType == DocumentType.JournalVoucher && x.SourceDocumentId == created.Id);

        Assert.Equal(3, entry.Lines.Count);
    }

    // ---------------------------------------------------------------------------------------
    // Phase 36 -- the *other* settlement path. Everything above allocates at Approve time; the
    // Allocate screens (phase 17) apply more of an already-Approved credit afterwards, and until
    // phase 36 that path added a PaymentAllocation row and touched the ledger not at all.
    // ---------------------------------------------------------------------------------------

    [Fact]
    public async Task A_further_allocation_books_the_same_forex_leg_as_an_approve_time_one()
    {
        // The phase's real claim is not "the number is 300" -- it is that the two doors agree. Same
        // invoice, same receipt, same two rates; one settled at Approve, one settled afterwards.
        var approveTimeDb = TestAppDbContext.Create();
        var approveTimeSeed = await SeedAsync(approveTimeDb);
        var approveTimeInvoice = await CreateAndApproveInvoiceAsync(approveTimeDb, approveTimeSeed, 100m, "USD", UsdRate);
        await CreateAndApprovePaymentAsync(approveTimeDb, approveTimeSeed, approveTimeInvoice, 100m, "USD", 130m);

        var furtherDb = TestAppDbContext.Create();
        var furtherSeed = await SeedAsync(furtherDb);
        var furtherInvoice = await CreateAndApproveInvoiceAsync(furtherDb, furtherSeed, 100m, "USD", UsdRate);
        var furtherPayment = await CreateAndApproveUnallocatedPaymentAsync(furtherDb, furtherSeed, 100m, "USD", 130m);
        await ApplyAsync(furtherDb, furtherSeed, furtherPayment, furtherInvoice, 100m);

        Assert.Equal(
            await AccountNetAsync(approveTimeDb, approveTimeSeed.ForexLossAccountId),
            await AccountNetAsync(furtherDb, furtherSeed.ForexLossAccountId));
        Assert.Equal(300m, await AccountNetAsync(furtherDb, furtherSeed.ForexLossAccountId));

        // And the reason the leg exists at all: AR is left flat either way.
        Assert.Equal(0m, await ArNetMovementAsync(furtherDb, furtherSeed));
        Assert.Equal(await ArNetMovementAsync(approveTimeDb, approveTimeSeed), await ArNetMovementAsync(furtherDb, furtherSeed));
    }

    [Fact]
    public async Task A_further_allocation_posts_its_forex_as_a_second_entry_that_balances_alone()
    {
        var db = TestAppDbContext.Create();
        var seed = await SeedAsync(db);
        var invoiceId = await CreateAndApproveInvoiceAsync(db, seed, 100m, "USD", UsdRate);
        var paymentId = await CreateAndApproveUnallocatedPaymentAsync(db, seed, 100m, "USD", 136m);

        await ApplyAsync(db, seed, paymentId, invoiceId, 100m);

        var entries = await db.GlJournalEntries.Include(x => x.Lines)
            .Where(x => x.SourceDocumentType == DocumentType.Payment && x.SourceDocumentId == paymentId)
            .ToListAsync();

        // The Approve-time entry, plus the correction. A posted entry is append-only (phase 16a),
        // so the correction cannot be folded into the first one.
        Assert.Equal(2, entries.Count);
        foreach (var entry in entries)
        {
            Assert.Equal(entry.Lines.Sum(x => x.Debit), entry.Lines.Sum(x => x.Credit));
        }

        // Settled at 136 against a booking of 133: 300 more rupees arrived, a gain (a credit, so
        // the account's net movement is negative in debit-minus-credit terms).
        Assert.Equal(-300m, await AccountNetAsync(db, seed.ForexGainAccountId));
        Assert.Equal(0m, await ArNetMovementAsync(db, seed));
    }

    [Fact]
    public async Task A_further_allocation_at_the_booking_rate_posts_nothing_at_all()
    {
        var db = TestAppDbContext.Create();
        var seed = await SeedAsync(db);
        var invoiceId = await CreateAndApproveInvoiceAsync(db, seed, 100m, "USD", UsdRate);
        var paymentId = await CreateAndApproveUnallocatedPaymentAsync(db, seed, 100m, "USD", UsdRate);

        await ApplyAsync(db, seed, paymentId, invoiceId, 100m);

        var entries = await db.GlJournalEntries
            .Where(x => x.SourceDocumentType == DocumentType.Payment && x.SourceDocumentId == paymentId)
            .CountAsync();

        // No difference, no entry -- the single-currency tenant's path, unchanged.
        Assert.Equal(1, entries);
    }

    [Fact]
    public async Task A_further_allocation_to_a_document_in_another_currency_is_refused()
    {
        // Phase 28 Decision F, now enforced on both doors rather than only at Approve.
        var db = TestAppDbContext.Create();
        var seed = await SeedAsync(db);
        var invoiceId = await CreateAndApproveInvoiceAsync(db, seed, 100m, "USD", UsdRate);
        var paymentId = await CreateAndApproveUnallocatedPaymentAsync(db, seed, 13300m, currencyCode: null, exchangeRate: null);

        var ex = await Assert.ThrowsAsync<ConflictException>(() => ApplyAsync(db, seed, paymentId, invoiceId, 13300m));

        Assert.Contains("its own currency", ex.Message, StringComparison.Ordinal);

        // And nothing was written on the way to the refusal.
        Assert.Equal(0, await db.PaymentAllocations.CountAsync(x => x.SourceId == paymentId));
    }

    [Fact]
    public async Task Voiding_a_payment_allocated_further_reverses_every_entry_it_posted()
    {
        var db = TestAppDbContext.Create();
        var seed = await SeedAsync(db);
        var invoiceId = await CreateAndApproveInvoiceAsync(db, seed, 100m, "USD", UsdRate);
        var paymentId = await CreateAndApproveUnallocatedPaymentAsync(db, seed, 100m, "USD", 130m);
        await ApplyAsync(db, seed, paymentId, invoiceId, 100m);

        await new VoidPaymentCommandHandler(db, new FakeCurrentUserService(Guid.NewGuid()))
            .Handle(new VoidPaymentCommand(seed.OrganizationId, paymentId), CancellationToken.None);

        // The forex leg goes back with the cash leg: a voided payment settled nothing, so it
        // realised nothing. AR is left holding the invoice's own 13,300 again.
        Assert.Equal(0m, await AccountNetAsync(db, seed.ForexLossAccountId));
        Assert.Equal(0m, await AccountNetAsync(db, seed.CashAccountId));
        Assert.Equal(13300m, await ArNetMovementAsync(db, seed));
    }

    private static async Task<decimal> AccountNetAsync(IAppDbContext db, Guid accountId)
    {
        var lines = await db.GlJournalEntries
            .Include(x => x.Lines)
            .SelectMany(x => x.Lines)
            .Where(x => x.AccountId == accountId)
            .ToListAsync();

        return lines.Sum(x => x.Debit) - lines.Sum(x => x.Credit);
    }

    private static async Task<Guid> CreateAndApproveUnallocatedPaymentAsync(
        IAppDbContext db, Seed seed, decimal amount, string? currencyCode, decimal? exchangeRate)
    {
        var created = await new CreatePaymentCommandHandler(db).Handle(
            new CreatePaymentCommand(
                seed.OrganizationId, seed.CustomerId, PaymentDirection.Received, new DateOnly(2026, 1, 2), null,
                seed.CashAccountId, amount, null, [])
            { CurrencyCode = currencyCode, ExchangeRate = exchangeRate },
            CancellationToken.None);

        var approved = await new ApprovePaymentCommandHandler(
            db, seed.NumberGenerator, new FakeCurrentUserService(Guid.NewGuid()), new PaymentPostingRule(), new GlCashBalancePolicy(db))
            .Handle(new ApprovePaymentCommand(seed.OrganizationId, created.Id), CancellationToken.None);

        return approved.Id;
    }

    private static Task ApplyAsync(IAppDbContext db, Seed seed, Guid paymentId, Guid invoiceId, decimal amount)
    {
        return new ApplyPaymentAllocationCommandHandler(db).Handle(
            new ApplyPaymentAllocationCommand(
                seed.OrganizationId, DocumentType.Payment, paymentId, null, DocumentType.Invoice, invoiceId, amount),
            CancellationToken.None);
    }

    // ---------------------------------------------------------------------------------------
    // Phase 36 -- what a contact *owes*, in one unit. Until this phase the contact-ledger family
    // summed a USD invoice and an NPR receipt as if they were the same number.
    // ---------------------------------------------------------------------------------------

    [Fact]
    public async Task A_foreign_invoice_ages_in_the_base_currency()
    {
        var db = TestAppDbContext.Create();
        var seed = await SeedAsync(db);
        await CreateAndApproveInvoiceAsync(db, seed, 100m, "USD", UsdRate);

        var asOf = new DateOnly(2026, 1, 31);
        var result = await new DocumentAgeQueryHandler(db, new FakeCurrentUserService(Guid.NewGuid())).Handle(
            new DocumentAgeQuery(seed.OrganizationId, ContactType.Customer, new DateOnly(2026, 1, 1), asOf),
            CancellationToken.None);

        // 100 USD at 133 -- the same 13,300 the general ledger booked, not a bare 100 that would
        // have been added to NPR figures elsewhere in the report.
        var row = Assert.Single(result.Rows);
        Assert.Equal(13_300m, row.Amount);
        Assert.Equal(13_300m, row.Balance);
    }

    [Fact]
    public async Task An_invoice_settled_at_a_different_rate_owes_nothing_afterwards()
    {
        // The reason a settlement folds at the rate of what it settles: the realised difference is
        // a P&L figure, not something the customer still owes.
        var db = TestAppDbContext.Create();
        var seed = await SeedAsync(db);
        var invoiceId = await CreateAndApproveInvoiceAsync(db, seed, 100m, "USD", UsdRate);
        await CreateAndApprovePaymentAsync(db, seed, invoiceId, 100m, "USD", 130m);

        var asOf = new DateOnly(2026, 1, 31);
        var ageing = await new DocumentAgeQueryHandler(db, new FakeCurrentUserService(Guid.NewGuid())).Handle(
            new DocumentAgeQuery(seed.OrganizationId, ContactType.Customer, new DateOnly(2026, 1, 1), asOf),
            CancellationToken.None);

        Assert.Empty(ageing.Rows);

        var statement = await new ContactStatementQueryHandler(db, new FakeCurrentUserService(Guid.NewGuid())).Handle(
            new ContactStatementQuery(
                seed.OrganizationId, ContactType.Customer, seed.CustomerId, new DateOnly(2026, 1, 1), asOf),
            CancellationToken.None);

        // 13,300 invoiced, 13,300 relieved -- the 300 loss is on the forex account, not here.
        Assert.Equal(0m, statement.ClosingBalance);
    }

    [Fact]
    public async Task The_credit_limit_compares_base_currency_against_base_currency()
    {
        var db = TestAppDbContext.Create();
        var seed = await SeedAsync(db);

        var customer = await db.Contacts.SingleAsync(x => x.Id == seed.CustomerId);
        customer.Update(customer.Name, null, null, null, null, null, 0m, creditLimit: 10_000m);
        await db.SaveChangesAsync(CancellationToken.None);

        // 100 USD is 13,300 NPR, which is past a 10,000 limit -- while the bare 100 this compared
        // before phase 36 sat comfortably inside it.
        var result = await new ContactCreditLimitPolicy(db).CheckAsync(
            seed.OrganizationId, seed.CustomerId, ExchangeRates.ToBase(100m, UsdRate), CancellationToken.None);

        Assert.Equal(13_300m, result.ProjectedBalance);
        Assert.NotEqual(CreditLimitStatus.Ok, result.Status);
    }

    private static async Task<decimal> ArNetMovementAsync(IAppDbContext db, Seed seed)
    {
        var lines = await db.GlJournalEntries
            .Include(x => x.Lines)
            .SelectMany(x => x.Lines)
            .Where(x => x.AccountId == seed.ArAccountId)
            .ToListAsync();

        return lines.Sum(x => x.Debit) - lines.Sum(x => x.Credit);
    }

    private sealed record Seed(
        Guid OrganizationId, FakeDocumentNumberGenerator NumberGenerator, Guid CustomerId, Guid WarehouseId,
        Guid ProductId, Guid CashAccountId, Guid ArAccountId, Guid SalesAccountId,
        Guid ForexGainAccountId, Guid ForexLossAccountId);

    private static async Task<Seed> SeedAsync(IAppDbContext db, bool configureForexAccounts = true)
    {
        var organizationId = Guid.NewGuid();
        var numberGenerator = new FakeDocumentNumberGenerator();

        var customer = await new CreateContactCommandHandler(db, numberGenerator).Handle(
            new CreateContactCommand(organizationId, ContactType.Customer, "Acme Traders", null, null, null, null, null, 0m),
            CancellationToken.None);
        var warehouse = await new CreateWarehouseCommandHandler(db).Handle(
            new CreateWarehouseCommand(organizationId, "Main Warehouse"), CancellationToken.None);
        var category = await new CreateProductCategoryCommandHandler(db).Handle(
            new CreateProductCategoryCommand(organizationId, "General", null), CancellationToken.None);
        var unit = await new CreateUnitOfMeasurementCommandHandler(db).Handle(
            new CreateUnitOfMeasurementCommand(organizationId, "Piece", "pc"), CancellationToken.None);
        var product = await new CreateProductCommandHandler(db, numberGenerator).Handle(
            new CreateProductCommand(
                organizationId, ProductType.Service, "Consulting", category.Id, unit.Id, null, true, 150m, 100m,
                VatRate.NoVat, 0, false),
            CancellationToken.None);

        var assetGroup = await new CreateAccountGroupCommandHandler(db).Handle(
            new CreateAccountGroupCommand(organizationId, "Current Assets", AccountRootType.Asset, null), CancellationToken.None);
        var liabilityGroup = await new CreateAccountGroupCommandHandler(db).Handle(
            new CreateAccountGroupCommand(organizationId, "Current Liabilities", AccountRootType.Liability, null), CancellationToken.None);
        var incomeGroup = await new CreateAccountGroupCommandHandler(db).Handle(
            new CreateAccountGroupCommand(organizationId, "Sales Income", AccountRootType.Income, null), CancellationToken.None);
        var expenseGroup = await new CreateAccountGroupCommandHandler(db).Handle(
            new CreateAccountGroupCommand(organizationId, "Indirect Expenses", AccountRootType.Expense, null), CancellationToken.None);

        var ar = await new CreateAccountCommandHandler(db, numberGenerator).Handle(
            new CreateAccountCommand(organizationId, "Accounts Receivable", assetGroup.Id), CancellationToken.None);
        var vatPayable = await new CreateAccountCommandHandler(db, numberGenerator).Handle(
            new CreateAccountCommand(organizationId, "VAT Payable", liabilityGroup.Id), CancellationToken.None);
        var sales = await new CreateAccountCommandHandler(db, numberGenerator).Handle(
            new CreateAccountCommand(organizationId, "Sales Revenue", incomeGroup.Id), CancellationToken.None);
        var cash = await new CreateAccountCommandHandler(db, numberGenerator).Handle(
            new CreateAccountCommand(organizationId, "Cash in Hand", assetGroup.Id), CancellationToken.None);

        // Named exactly as the reference product's own account is ("Forex Gain", Income, under a
        // "Foreign Exchange Gain" group -- confirmed live 2026-09-04). The Loss counterpart has no
        // live equivalent at all; see TenantSettings.DefaultForexGainAccountId for why we ship one.
        var forexGain = await new CreateAccountCommandHandler(db, numberGenerator).Handle(
            new CreateAccountCommand(organizationId, "Forex Gain", incomeGroup.Id), CancellationToken.None);
        var forexLoss = await new CreateAccountCommandHandler(db, numberGenerator).Handle(
            new CreateAccountCommand(organizationId, "Forex Loss", expenseGroup.Id), CancellationToken.None);

        var settings = TenantSettings.CreateDefault(organizationId);
        settings.SetAccountingDefaults(
            sales.Id, ar.Id, vatPayable.Id, null, null, null, null,
            configureForexAccounts ? forexGain.Id : null,
            configureForexAccounts ? forexLoss.Id : null);
        db.TenantSettings.Add(settings);
        db.Currencies.Add(Currency.CreateBase(organizationId));
        await db.SaveChangesAsync(CancellationToken.None);

        return new Seed(
            organizationId, numberGenerator, customer.Id, warehouse.Id, product.Id, cash.Id, ar.Id, sales.Id,
            forexGain.Id, forexLoss.Id);
    }

    private static async Task<Guid> CreateAndApproveInvoiceAsync(
        IAppDbContext db, Seed seed, decimal amount, string? currencyCode, decimal? exchangeRate)
    {
        var created = await new CreateInvoiceCommandHandler(db).Handle(
            new CreateInvoiceCommand(
                seed.OrganizationId, seed.CustomerId, seed.WarehouseId, new DateOnly(2026, 1, 1), null,
                [new InvoiceLineInput(seed.ProductId, 1m, amount, VatRate.NoVat)])
            { CurrencyCode = currencyCode, ExchangeRate = exchangeRate },
            CancellationToken.None);

        var stockLedgerService = new StockLedgerService(db);
        var approved = await new ApproveInvoiceCommandHandler(
            db, seed.NumberGenerator, new FakeCurrentUserService(Guid.NewGuid()), new InvoicePostingRule(),
            new FifoStockAvailabilityPolicy(db, stockLedgerService), stockLedgerService, new ContactCreditLimitPolicy(db))
            .Handle(new ApproveInvoiceCommand(seed.OrganizationId, created.Id, OverrideWarning: false), CancellationToken.None);

        return approved.Id;
    }

    private static async Task<Guid> CreateAndApprovePaymentAsync(
        IAppDbContext db, Seed seed, Guid invoiceId, decimal amount, string? currencyCode, decimal? exchangeRate)
    {
        var created = await new CreatePaymentCommandHandler(db).Handle(
            new CreatePaymentCommand(
                seed.OrganizationId, seed.CustomerId, PaymentDirection.Received, new DateOnly(2026, 1, 2), null,
                seed.CashAccountId, amount, null,
                [new PaymentAllocationInput(DocumentType.Invoice, invoiceId, amount)])
            { CurrencyCode = currencyCode, ExchangeRate = exchangeRate },
            CancellationToken.None);

        var approved = await new ApprovePaymentCommandHandler(
            db, seed.NumberGenerator, new FakeCurrentUserService(Guid.NewGuid()), new PaymentPostingRule(), new GlCashBalancePolicy(db))
            .Handle(new ApprovePaymentCommand(seed.OrganizationId, created.Id), CancellationToken.None);

        return approved.Id;
    }
}
