using ErpApp.Application.Contacts.Queries.ContactAgeingSummary;
using ErpApp.Application.Contacts.Queries.DocumentAge;
using ErpApp.Application.UnitTests.TestSupport;
using ErpApp.Domain.Common;
using ErpApp.Domain.Contacts;
using ErpApp.Domain.Payments;

namespace ErpApp.Application.UnitTests.Contacts;

/// <summary>
/// Phase 36's exit bar for the ageing work: <b>the two ageing reports agree on the same data</b> --
/// not two tests that each look right alone.
///
/// <para>Ageing Summary (phase 9) buckets per contact; Invoice Age / Purchase Bill Age (phase 26b)
/// lists per document. They were two implementations of one netting and drifted twice: over
/// JournalVoucher-sourced allocations, and over bucketing from the due date rather than the
/// document date. Phase 31 patched both by editing the older handler to match the newer one, which
/// left them agreeing by coincidence -- and still disagreeing about which documents are ageable at
/// all. Phase 36 made them a listing and a bucketing of the same rows
/// (<c>OutstandingDocumentReader</c>); these tests are what says so, on data that exercises every
/// candidate type: an opening balance, a trade document, a contact-tagged Journal Voucher, a
/// partly-paid bill, a settlement sourced from a voucher line, and a linked reversal.</para>
/// </summary>
public class AgeingReportsAgreeTests
{
    private static readonly DateOnly From = new(2026, 1, 1);
    private static readonly DateOnly AsOf = new(2026, 6, 30);

    private static DocumentAgeQueryHandler DocumentAge(ErpApp.Application.Common.Persistence.IAppDbContext db) =>
        new(db, new FakeCurrentUserService(Guid.NewGuid()));

    private static ContactAgeingSummaryQueryHandler Summary(ErpApp.Application.Common.Persistence.IAppDbContext db) =>
        new(db, new FakeCurrentUserService(Guid.NewGuid()));

    [Fact]
    public async Task The_customer_buckets_total_exactly_what_invoice_age_lists()
    {
        var db = TestAppDbContext.Create();
        var seed = await TradeReportSeed.CreateAsync(db, customerOpeningBalance: 2_000m);

        // An invoice settled in part by a payment, a second settled in part by a voucher-sourced
        // allocation, one fully settled (so it must appear in neither report), a credit note, and a
        // contact-tagged journal voucher that is itself an outstanding item.
        var partlyPaid = await seed.ApproveInvoiceAsync(db, AsOf.AddDays(-95), 1_000m);
        await seed.ApprovePaymentAsync(
            db, AsOf.AddDays(-10), PaymentDirection.Received, seed.CustomerId,
            [(DocumentType.Invoice, partlyPaid.Id, 400m)]);

        var settled = await seed.ApproveInvoiceAsync(db, AsOf.AddDays(-70), 500m);
        await seed.ApprovePaymentAsync(
            db, AsOf.AddDays(-5), PaymentDirection.Received, seed.CustomerId,
            [(DocumentType.Invoice, settled.Id, 500m)]);

        // A linked credit note must match a line of its source by (product, rate, VAT), so the
        // invoice is two units of 400 and one of them comes back.
        var returned = await seed.ApproveInvoiceAsync(db, AsOf.AddDays(-40), 400m, quantity: 2m);
        await seed.ApproveCreditNoteAsync(db, AsOf.AddDays(-20), 1m, 400m, returned.Id);

        await seed.ApproveContactJournalVoucherAsync(
            db, AsOf.AddDays(-15), seed.CustomerId, seed.ArAccountId, 250m, debitContact: true);

        var rows = await DocumentAge(db).Handle(
            new DocumentAgeQuery(seed.OrganizationId, ContactType.Customer, From, AsOf), CancellationToken.None);
        var buckets = await Summary(db).Handle(
            new ContactAgeingSummaryQuery(seed.OrganizationId, ContactType.Customer, AsOf), CancellationToken.None);

        var bucketTotal = buckets.TotalDays1To30 + buckets.TotalDays31To60 + buckets.TotalDays61To90 + buckets.TotalDays91Plus;

        // 600 (part-paid) + 400 (net of the credit note) + 250 (voucher) + 2,000 (opening balance).
        Assert.Equal(3_250m, rows.TotalBalance);
        Assert.Equal(rows.TotalBalance, bucketTotal);
    }

    [Fact]
    public async Task Every_bucket_is_the_sum_of_the_document_rows_that_fall_in_it()
    {
        // Agreement in total can hide two compensating errors; this pins the partition itself.
        var db = TestAppDbContext.Create();
        var seed = await TradeReportSeed.CreateAsync(db, customerOpeningBalance: 2_000m);

        await seed.ApproveInvoiceAsync(db, AsOf.AddDays(-95), 1_000m);
        await seed.ApproveInvoiceAsync(db, AsOf.AddDays(-75), 700m);
        await seed.ApproveInvoiceAsync(db, AsOf.AddDays(-45), 600m);
        await seed.ApproveInvoiceAsync(db, AsOf.AddDays(-5), 300m);
        await seed.ApproveContactJournalVoucherAsync(
            db, AsOf.AddDays(-62), seed.CustomerId, seed.ArAccountId, 250m, debitContact: true);

        var rows = await DocumentAge(db).Handle(
            new DocumentAgeQuery(seed.OrganizationId, ContactType.Customer, From, AsOf, ExportAll: true),
            CancellationToken.None);
        var buckets = await Summary(db).Handle(
            new ContactAgeingSummaryQuery(seed.OrganizationId, ContactType.Customer, AsOf), CancellationToken.None);

        decimal Bucket(Func<int, bool> predicate) => rows.Rows.Where(x => predicate(x.AgeDays)).Sum(x => x.Balance);

        Assert.Equal(Bucket(age => age <= 30), buckets.TotalDays1To30);
        Assert.Equal(Bucket(age => age is > 30 and <= 60), buckets.TotalDays31To60);
        Assert.Equal(Bucket(age => age is > 60 and <= 90), buckets.TotalDays61To90);
        Assert.Equal(Bucket(age => age > 90), buckets.TotalDays91Plus);

        // The opening balance and the not-yet-due invoice both land in the first bucket, so this is
        // only meaningful if every bucket carries something.
        Assert.All(
            new[] { buckets.TotalDays1To30, buckets.TotalDays31To60, buckets.TotalDays61To90, buckets.TotalDays91Plus },
            total => Assert.True(total > 0));
    }

    [Fact]
    public async Task The_supplier_side_agrees_the_same_way()
    {
        var db = TestAppDbContext.Create();
        var seed = await TradeReportSeed.CreateAsync(db);

        // Four units of 500, one of which is returned -- a linked debit note has to match a line of
        // its source bill by (product, rate, VAT).
        var bill = await seed.ApprovePurchaseBillAsync(db, AsOf.AddDays(-50), 500m, quantity: 4m);
        await seed.ApprovePaymentAsync(
            db, AsOf.AddDays(-4), PaymentDirection.Paid, seed.SupplierId,
            [(DocumentType.PurchaseBill, bill.Id, 750m)]);
        await seed.ApproveDebitNoteAsync(db, AsOf.AddDays(-3), 1m, 500m, bill.Id);
        await seed.ApproveContactJournalVoucherAsync(
            db, AsOf.AddDays(-35), seed.SupplierId, seed.ApAccountId, 400m, debitContact: false);

        var rows = await DocumentAge(db).Handle(
            new DocumentAgeQuery(seed.OrganizationId, ContactType.Supplier, From, AsOf), CancellationToken.None);
        var buckets = await Summary(db).Handle(
            new ContactAgeingSummaryQuery(seed.OrganizationId, ContactType.Supplier, AsOf), CancellationToken.None);

        var bucketTotal = buckets.TotalDays1To30 + buckets.TotalDays31To60 + buckets.TotalDays61To90 + buckets.TotalDays91Plus;

        Assert.Equal(1_150m, rows.TotalBalance); // 2,000 - 750 paid - 500 returned + 400 voucher
        Assert.Equal(rows.TotalBalance, bucketTotal);
    }

    [Fact]
    public async Task An_allocation_sourced_from_a_journal_voucher_line_reduces_both_reports_alike()
    {
        // The first divergence phase 31 patched, now unrepresentable: one reader answers both.
        var db = TestAppDbContext.Create();
        var seed = await TradeReportSeed.CreateAsync(db);

        var invoice = await seed.ApproveInvoiceAsync(db, AsOf.AddDays(-20), 1_000m);

        // A voucher crediting the customer's control account is a credit that can settle an invoice.
        var voucher = await seed.ApproveContactJournalVoucherAsync(
            db, AsOf.AddDays(-10), seed.CustomerId, seed.ArAccountId, 300m, debitContact: false);

        var line = db.JournalVoucherLines.Single(x => x.JournalVoucherId == voucher.Id && x.ContactId != null);

        await new ErpApp.Application.Payments.Commands.ApplyPaymentAllocation.ApplyPaymentAllocationCommandHandler(db)
            .Handle(
                new ErpApp.Application.Payments.Commands.ApplyPaymentAllocation.ApplyPaymentAllocationCommand(
                    seed.OrganizationId, DocumentType.JournalVoucher, line.Id, voucher.Id,
                    DocumentType.Invoice, invoice.Id, 300m),
                CancellationToken.None);

        var rows = await DocumentAge(db).Handle(
            new DocumentAgeQuery(seed.OrganizationId, ContactType.Customer, From, AsOf), CancellationToken.None);
        var buckets = await Summary(db).Handle(
            new ContactAgeingSummaryQuery(seed.OrganizationId, ContactType.Customer, AsOf), CancellationToken.None);

        var bucketTotal = buckets.TotalDays1To30 + buckets.TotalDays31To60 + buckets.TotalDays61To90 + buckets.TotalDays91Plus;

        var invoiceRow = Assert.Single(rows.Rows.Where(x => x.DocumentId == invoice.Id));
        Assert.Equal(300m, invoiceRow.Paid);
        Assert.Equal(700m, invoiceRow.Balance);
        Assert.Equal(rows.TotalBalance, bucketTotal);
    }
}
