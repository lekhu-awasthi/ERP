using ErpApp.Application.Contacts.Queries.DocumentAge;
using ErpApp.Application.UnitTests.TestSupport;
using ErpApp.Domain.Common;
using ErpApp.Domain.Contacts;
using ErpApp.Domain.Payments;

namespace ErpApp.Application.UnitTests.Contacts;

public class DocumentAgeQueryHandlerTests
{
    private static readonly DateOnly From = new(2026, 1, 1);
    private static readonly DateOnly AsOf = new(2026, 6, 30);

    [Fact]
    public async Task An_unpaid_invoice_is_overdue_with_an_age_measured_from_its_due_date()
    {
        var db = TestAppDbContext.Create();
        var seed = await TradeReportSeed.CreateAsync(db);

        await seed.ApproveInvoiceAsync(db, AsOf.AddDays(-40), 1_200m);

        var query = new DocumentAgeQuery(seed.OrganizationId, ContactType.Customer, From, AsOf);
        Assert.Equal("Reports.InvoiceAge.View", query.PermissionKey);

        var result = await new DocumentAgeQueryHandler(db).Handle(query, CancellationToken.None);

        var row = Assert.Single(result.Rows);
        Assert.Equal(AgeableDocumentType.Invoice, row.DocumentType);
        Assert.Equal(1_200m, row.Amount);
        Assert.Equal(0m, row.Paid);
        Assert.Equal(1_200m, row.Balance);
        Assert.Equal(DocumentAgeRowDto.Overdue, row.Status);
        Assert.Equal(40, row.AgeDays);
        Assert.Equal("Acme Traders", row.ContactName);
        Assert.Equal("Key Accounts", row.ContactGroupName);

        // Invoice stores no due date in this codebase, so Due Date is the document's own date.
        Assert.Equal(row.Date, row.DueDate);

        Assert.Equal(1_200m, result.TotalAmount);
        Assert.Equal(0m, result.TotalPaid);
        Assert.Equal(1_200m, result.TotalBalance);
    }

    /// <summary>A document not yet due reports Current and age zero -- never a negative age.</summary>
    [Fact]
    public async Task A_document_dated_on_the_as_of_date_is_current_with_age_zero()
    {
        var db = TestAppDbContext.Create();
        var seed = await TradeReportSeed.CreateAsync(db);

        await seed.ApproveInvoiceAsync(db, AsOf, 500m);

        var result = await new DocumentAgeQueryHandler(db).Handle(
            new DocumentAgeQuery(seed.OrganizationId, ContactType.Customer, From, AsOf), CancellationToken.None);

        var row = Assert.Single(result.Rows);
        Assert.Equal(DocumentAgeRowDto.Current, row.Status);
        Assert.Equal(0, row.AgeDays);
    }

    [Fact]
    public async Task Payments_and_linked_credit_notes_both_reduce_the_balance_and_a_settled_document_disappears()
    {
        var db = TestAppDbContext.Create();
        var seed = await TradeReportSeed.CreateAsync(db);

        var partly = await seed.ApproveInvoiceAsync(db, AsOf.AddDays(-30), 1_000m);
        await seed.ApprovePaymentAsync(
            db, AsOf.AddDays(-10), PaymentDirection.Received, seed.CustomerId,
            [(DocumentType.Invoice, partly.Id, 400m)]);

        var reversed = await seed.ApproveInvoiceAsync(db, AsOf.AddDays(-20), 800m);
        await seed.ApproveCreditNoteAsync(db, AsOf.AddDays(-5), 0.25m, 800m, reversed.Id);

        var settled = await seed.ApproveInvoiceAsync(db, AsOf.AddDays(-15), 600m);
        await seed.ApprovePaymentAsync(
            db, AsOf.AddDays(-1), PaymentDirection.Received, seed.CustomerId,
            [(DocumentType.Invoice, settled.Id, 600m)]);

        var result = await new DocumentAgeQueryHandler(db).Handle(
            new DocumentAgeQuery(seed.OrganizationId, ContactType.Customer, From, AsOf), CancellationToken.None);

        Assert.Equal(2, result.Rows.Count);

        var partlyRow = result.Rows.Single(x => x.DocumentId == partly.Id);
        Assert.Equal(400m, partlyRow.Paid);
        Assert.Equal(600m, partlyRow.Balance);

        var reversedRow = result.Rows.Single(x => x.DocumentId == reversed.Id);
        Assert.Equal(200m, reversedRow.Paid);
        Assert.Equal(600m, reversedRow.Balance);

        Assert.DoesNotContain(result.Rows, x => x.DocumentId == settled.Id);
        Assert.Equal(1_200m, result.TotalBalance);
    }

    /// <summary>The finding that decided this report's document set: a contact-tagged Journal Voucher
    /// is an ageable document. Confirmed live -- the reference product's Txn Type filter names it.</summary>
    [Fact]
    public async Task A_contact_tagged_journal_voucher_is_an_ageable_document()
    {
        var db = TestAppDbContext.Create();
        var seed = await TradeReportSeed.CreateAsync(db);

        var voucher = await seed.ApproveContactJournalVoucherAsync(
            db, AsOf.AddDays(-12), seed.CustomerId, seed.ArAccountId, 350m, debitContact: true);

        // A voucher crediting the customer is a credit, not an ageable item -- it belongs to the
        // balance summary, not here.
        await seed.ApproveContactJournalVoucherAsync(
            db, AsOf.AddDays(-11), seed.SecondCustomerId, seed.ArAccountId, 90m, debitContact: false);

        var result = await new DocumentAgeQueryHandler(db).Handle(
            new DocumentAgeQuery(seed.OrganizationId, ContactType.Customer, From, AsOf), CancellationToken.None);

        var row = Assert.Single(result.Rows);
        Assert.Equal(AgeableDocumentType.JournalVoucher, row.DocumentType);
        Assert.Equal(voucher.Id, row.DocumentId);
        Assert.Equal(voucher.Code, row.Number);
        Assert.Equal("JV-REF", row.ReferenceNo);
        Assert.Equal(350m, row.Balance);
        Assert.Equal(12, row.AgeDays);
    }

    /// <summary>A contact's opening balance is an ageable item with no document behind it, so it
    /// carries the literal label and ages from the as-of date.</summary>
    [Fact]
    public async Task An_opening_balance_appears_as_its_own_row_dated_on_the_as_of_date()
    {
        var db = TestAppDbContext.Create();
        var seed = await TradeReportSeed.CreateAsync(db, customerOpeningBalance: 2_500m);

        var result = await new DocumentAgeQueryHandler(db).Handle(
            new DocumentAgeQuery(seed.OrganizationId, ContactType.Customer, From, AsOf), CancellationToken.None);

        var row = Assert.Single(result.Rows);
        Assert.Equal(AgeableDocumentType.OpeningBalance, row.DocumentType);
        Assert.Equal("Opening Balance", row.Number);
        Assert.Null(row.ReferenceNo);
        Assert.Equal(2_500m, row.Balance);
        Assert.Equal(AsOf, row.Date);
        Assert.Equal(DocumentAgeRowDto.Current, row.Status);
        Assert.Equal(0, row.AgeDays);
    }

    [Fact]
    public async Task The_document_type_filter_narrows_the_rows_and_the_totals_with_them()
    {
        var db = TestAppDbContext.Create();
        var seed = await TradeReportSeed.CreateAsync(db, customerOpeningBalance: 100m);

        await seed.ApproveInvoiceAsync(db, AsOf.AddDays(-10), 700m);
        await seed.ApproveContactJournalVoucherAsync(
            db, AsOf.AddDays(-9), seed.CustomerId, seed.ArAccountId, 300m, debitContact: true);

        var all = await new DocumentAgeQueryHandler(db).Handle(
            new DocumentAgeQuery(seed.OrganizationId, ContactType.Customer, From, AsOf), CancellationToken.None);
        Assert.Equal(3, all.Rows.Count);
        Assert.Equal(1_100m, all.TotalBalance);

        var invoicesOnly = await new DocumentAgeQueryHandler(db).Handle(
            new DocumentAgeQuery(
                seed.OrganizationId, ContactType.Customer, From, AsOf,
                DocumentTypes: [AgeableDocumentType.Invoice]),
            CancellationToken.None);

        var row = Assert.Single(invoicesOnly.Rows);
        Assert.Equal(AgeableDocumentType.Invoice, row.DocumentType);
        Assert.Equal(700m, invoicesOnly.TotalBalance);
    }

    [Fact]
    public async Task The_contact_filter_narrows_to_one_contacts_documents()
    {
        var db = TestAppDbContext.Create();
        var seed = await TradeReportSeed.CreateAsync(db);

        await seed.ApproveInvoiceAsync(db, AsOf.AddDays(-10), 700m);
        await seed.ApproveInvoiceAsync(db, AsOf.AddDays(-10), 250m, seed.SecondCustomerId);

        var result = await new DocumentAgeQueryHandler(db).Handle(
            new DocumentAgeQuery(seed.OrganizationId, ContactType.Customer, From, AsOf, seed.SecondCustomerId),
            CancellationToken.None);

        var row = Assert.Single(result.Rows);
        Assert.Equal("Beacon Retail", row.ContactName);
        Assert.Equal(250m, row.Balance);
    }

    [Fact]
    public async Task The_payable_side_ages_purchase_bills_net_of_debit_notes_under_its_own_key()
    {
        var db = TestAppDbContext.Create();
        var seed = await TradeReportSeed.CreateAsync(db);

        var bill = await seed.ApprovePurchaseBillAsync(db, AsOf.AddDays(-25), 4_000m);
        await seed.ApproveDebitNoteAsync(db, AsOf.AddDays(-5), 0.1m, 4_000m, bill.Id);

        var query = new DocumentAgeQuery(seed.OrganizationId, ContactType.Supplier, From, AsOf);
        Assert.Equal("Reports.PurchaseBillAge.View", query.PermissionKey);

        var result = await new DocumentAgeQueryHandler(db).Handle(query, CancellationToken.None);

        var row = Assert.Single(result.Rows);
        Assert.Equal(AgeableDocumentType.PurchaseBill, row.DocumentType);
        Assert.Equal(4_000m, row.Amount);
        Assert.Equal(400m, row.Paid);
        Assert.Equal(3_600m, row.Balance);
        Assert.Equal(25, row.AgeDays);
        Assert.Equal("Global Supplies", row.ContactName);
    }

    /// <summary>As with the balance summary, only the as-of date bounds the document set -- a
    /// document older than the stated period start is still outstanding and still shown.</summary>
    [Fact]
    public async Task A_document_older_than_the_period_start_is_still_aged()
    {
        var db = TestAppDbContext.Create();
        var seed = await TradeReportSeed.CreateAsync(db);

        await seed.ApproveInvoiceAsync(db, new DateOnly(2025, 2, 1), 90m);
        await seed.ApproveInvoiceAsync(db, new DateOnly(2026, 9, 1), 60m); // after the as-of date

        var result = await new DocumentAgeQueryHandler(db).Handle(
            new DocumentAgeQuery(seed.OrganizationId, ContactType.Customer, From, AsOf), CancellationToken.None);

        var row = Assert.Single(result.Rows);
        Assert.Equal(90m, row.Balance);
        Assert.Equal(new DateOnly(2025, 2, 1), row.Date);
    }

    /// <summary>Rows are ordered oldest-due-first, the order an ageing report is read in.</summary>
    [Fact]
    public async Task Rows_are_ordered_oldest_due_first()
    {
        var db = TestAppDbContext.Create();
        var seed = await TradeReportSeed.CreateAsync(db);

        await seed.ApproveInvoiceAsync(db, AsOf.AddDays(-5), 10m);
        await seed.ApproveInvoiceAsync(db, AsOf.AddDays(-50), 20m);
        await seed.ApproveInvoiceAsync(db, AsOf.AddDays(-25), 30m);

        var result = await new DocumentAgeQueryHandler(db).Handle(
            new DocumentAgeQuery(seed.OrganizationId, ContactType.Customer, From, AsOf), CancellationToken.None);

        Assert.Equal([50, 25, 5], result.Rows.Select(x => x.AgeDays));
    }
}
