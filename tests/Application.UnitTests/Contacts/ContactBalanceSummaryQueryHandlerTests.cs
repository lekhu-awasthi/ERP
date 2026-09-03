using ErpApp.Application.Contacts.Queries.ContactBalanceSummary;
using ErpApp.Application.Contacts.Queries.ContactStatement;
using ErpApp.Application.UnitTests.TestSupport;
using ErpApp.Domain.Contacts;
using ErpApp.Domain.Payments;

namespace ErpApp.Application.UnitTests.Contacts;

public class ContactBalanceSummaryQueryHandlerTests
{
    private static readonly DateOnly From = new(2026, 1, 1);
    private static readonly DateOnly To = new(2026, 6, 30);

    [Fact]
    public async Task Customer_closing_balance_nets_invoices_credit_notes_receipts_and_the_opening_balance()
    {
        var db = TestAppDbContext.Create();
        var seed = await TradeReportSeed.CreateAsync(db, customerOpeningBalance: 1_000m);

        var invoice = await seed.ApproveInvoiceAsync(db, new DateOnly(2026, 2, 1), 5_000m);
        await seed.ApproveCreditNoteAsync(db, new DateOnly(2026, 3, 1), 0.2m, 5_000m, invoice.Id);
        await seed.ApprovePaymentAsync(
            db, new DateOnly(2026, 4, 1), PaymentDirection.Received, seed.CustomerId,
            [(ErpApp.Domain.Common.DocumentType.Invoice, invoice.Id, 500m)]);

        var query = new ContactBalanceSummaryQuery(seed.OrganizationId, ContactType.Customer, From, To);
        Assert.Equal("Reports.CustomerReceivableSummary.View", query.PermissionKey);

        var result = await new ContactBalanceSummaryQueryHandler(db).Handle(query, CancellationToken.None);

        var row = Assert.Single(result.Rows);
        Assert.Equal("Acme Traders", row.ContactName);
        Assert.Equal("Key Accounts", row.ContactGroupName);

        // 1,000 opening + 5,000 invoice - 1,000 credit note - 500 receipt.
        Assert.Equal(4_500m, row.ClosingBalance);
        Assert.Equal("DR", row.BalanceType);
        Assert.Equal(4_500m, result.TotalClosingBalance);
    }

    /// <summary>
    /// The behaviour this phase added to <see cref="ContactLedgerReader"/>: a Journal Voucher line
    /// tagged with a contact moves that contact's balance. Before phase 26b it moved the general
    /// ledger and nothing else.
    /// </summary>
    [Fact]
    public async Task A_contact_tagged_journal_voucher_moves_the_balance_in_the_direction_of_its_own_side()
    {
        var db = TestAppDbContext.Create();
        var seed = await TradeReportSeed.CreateAsync(db);

        // Debit the customer's control account -> the customer owes 750 more.
        await seed.ApproveContactJournalVoucherAsync(
            db, new DateOnly(2026, 2, 10), seed.CustomerId, seed.ArAccountId, 750m, debitContact: true);

        // Credit it for a different customer -> that one is 200 in credit.
        await seed.ApproveContactJournalVoucherAsync(
            db, new DateOnly(2026, 2, 11), seed.SecondCustomerId, seed.ArAccountId, 200m, debitContact: false);

        var result = await new ContactBalanceSummaryQueryHandler(db).Handle(
            new ContactBalanceSummaryQuery(seed.OrganizationId, ContactType.Customer, From, To),
            CancellationToken.None);

        Assert.Equal(2, result.Rows.Count);

        var acme = result.Rows.Single(x => x.ContactName == "Acme Traders");
        Assert.Equal(750m, acme.ClosingBalance);
        Assert.Equal("DR", acme.BalanceType);

        var beacon = result.Rows.Single(x => x.ContactName == "Beacon Retail");
        Assert.Equal(-200m, beacon.ClosingBalance);
        Assert.Equal("CR", beacon.BalanceType);

        Assert.Equal(550m, result.TotalClosingBalance);
    }

    /// <summary>A supplier-tagged JV must not appear in the customer report, and vice versa --
    /// the ledger reader filters tagged lines by the contact's own type.</summary>
    [Fact]
    public async Task A_supplier_tagged_journal_voucher_never_reaches_the_customer_report()
    {
        var db = TestAppDbContext.Create();
        var seed = await TradeReportSeed.CreateAsync(db);

        await seed.ApproveContactJournalVoucherAsync(
            db, new DateOnly(2026, 2, 10), seed.SupplierId, seed.ApAccountId, 900m, debitContact: false);

        var customers = await new ContactBalanceSummaryQueryHandler(db).Handle(
            new ContactBalanceSummaryQuery(seed.OrganizationId, ContactType.Customer, From, To),
            CancellationToken.None);
        Assert.Empty(customers.Rows);

        var suppliers = await new ContactBalanceSummaryQueryHandler(db).Handle(
            new ContactBalanceSummaryQuery(seed.OrganizationId, ContactType.Supplier, From, To),
            CancellationToken.None);

        var row = Assert.Single(suppliers.Rows);
        Assert.Equal("Global Supplies", row.ContactName);
        Assert.Equal(900m, row.ClosingBalance);
        Assert.Equal("CR", row.BalanceType);
    }

    [Fact]
    public async Task Supplier_payable_uses_the_supplier_permission_key_and_nets_bills_against_debit_notes()
    {
        var db = TestAppDbContext.Create();
        var seed = await TradeReportSeed.CreateAsync(db);

        var bill = await seed.ApprovePurchaseBillAsync(db, new DateOnly(2026, 2, 1), 2_000m);
        await seed.ApproveDebitNoteAsync(db, new DateOnly(2026, 3, 1), 0.25m, 2_000m, bill.Id);

        var query = new ContactBalanceSummaryQuery(seed.OrganizationId, ContactType.Supplier, From, To);
        Assert.Equal("Reports.SupplierPayableSummary.View", query.PermissionKey);

        var result = await new ContactBalanceSummaryQueryHandler(db).Handle(query, CancellationToken.None);

        var row = Assert.Single(result.Rows);
        Assert.Equal(1_500m, row.ClosingBalance);
        Assert.Equal("CR", row.BalanceType);
    }

    /// <summary>A contact whose activity nets to exactly zero is absent, not a row of zeroes.</summary>
    [Fact]
    public async Task A_contact_settled_to_zero_is_omitted_entirely()
    {
        var db = TestAppDbContext.Create();
        var seed = await TradeReportSeed.CreateAsync(db);

        var invoice = await seed.ApproveInvoiceAsync(db, new DateOnly(2026, 2, 1), 400m);
        await seed.ApprovePaymentAsync(
            db, new DateOnly(2026, 2, 20), PaymentDirection.Received, seed.CustomerId,
            [(ErpApp.Domain.Common.DocumentType.Invoice, invoice.Id, 400m)]);

        var result = await new ContactBalanceSummaryQueryHandler(db).Handle(
            new ContactBalanceSummaryQuery(seed.OrganizationId, ContactType.Customer, From, To),
            CancellationToken.None);

        Assert.Empty(result.Rows);
        Assert.Equal(0m, result.TotalClosingBalance);
    }

    [Fact]
    public async Task The_contact_group_filter_narrows_the_rows_and_the_total_with_them()
    {
        var db = TestAppDbContext.Create();
        var seed = await TradeReportSeed.CreateAsync(db);

        await seed.ApproveInvoiceAsync(db, new DateOnly(2026, 2, 1), 300m);
        await seed.ApproveInvoiceAsync(db, new DateOnly(2026, 2, 2), 700m, seed.SecondCustomerId);

        var all = await new ContactBalanceSummaryQueryHandler(db).Handle(
            new ContactBalanceSummaryQuery(seed.OrganizationId, ContactType.Customer, From, To),
            CancellationToken.None);
        Assert.Equal(2, all.Rows.Count);
        Assert.Equal(1_000m, all.TotalClosingBalance);

        var grouped = await new ContactBalanceSummaryQueryHandler(db).Handle(
            new ContactBalanceSummaryQuery(seed.OrganizationId, ContactType.Customer, From, To, seed.CustomerGroupId),
            CancellationToken.None);

        var row = Assert.Single(grouped.Rows);
        Assert.Equal("Acme Traders", row.ContactName);
        Assert.Equal(300m, grouped.TotalClosingBalance);
    }

    /// <summary>The period's From date does not filter -- only ToDate cuts the ledger off, because a
    /// closing balance is an as-of figure. Confirmed live: rows from a year before the stated period
    /// start still contributed.</summary>
    [Fact]
    public async Task Only_the_ToDate_bounds_the_balance()
    {
        var db = TestAppDbContext.Create();
        var seed = await TradeReportSeed.CreateAsync(db);

        await seed.ApproveInvoiceAsync(db, new DateOnly(2025, 3, 1), 250m); // long before FromDate
        await seed.ApproveInvoiceAsync(db, new DateOnly(2026, 8, 1), 999m); // after ToDate

        var result = await new ContactBalanceSummaryQueryHandler(db).Handle(
            new ContactBalanceSummaryQuery(seed.OrganizationId, ContactType.Customer, From, To),
            CancellationToken.None);

        var row = Assert.Single(result.Rows);
        Assert.Equal(250m, row.ClosingBalance);
    }
}
