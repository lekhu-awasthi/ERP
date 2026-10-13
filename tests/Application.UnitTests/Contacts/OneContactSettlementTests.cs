using ErpApp.Application.Common.Persistence;
using ErpApp.Application.Contacts.Commands.CreateContact;
using ErpApp.Application.Contacts.Queries.DocumentAge;
using ErpApp.Application.UnitTests.Pos;
using ErpApp.Application.UnitTests.TestSupport;
using ErpApp.Domain.Catalog;
using ErpApp.Domain.Common;
using ErpApp.Domain.Contacts;
using ErpApp.Domain.Payments;

namespace ErpApp.Application.UnitTests.Contacts;

/// <summary>
/// Phase 90 -- when a payment form, the FIFO Suggest or a till refund asks about <b>one contact</b>,
/// <c>OutstandingDocumentReader</c> narrows every settlement query (allocations from payments and from
/// vouchers, till tenders and their change, credit and debit notes and a refund's payouts) through that
/// contact's documents, where it used to read the whole organization's. The narrowing may only ever drop
/// rows that belong to somebody else, so the invariant is: <b>one contact's read equals the
/// organization-wide read, filtered to that contact, row for row</b>. Each world gives two contacts on a
/// side every kind of settlement, so a filter that dropped one of them would show as a wrong Paid.
/// </summary>
public sealed class OneContactSettlementTests
{
    private static readonly DateOnly AsOf = new(2026, 6, 30);

    [Fact]
    public async Task Each_contacts_own_read_equals_the_organization_wide_read_of_that_contact()
    {
        var db = TestAppDbContext.Create();
        var seed = await TradeReportSeed.CreateAsync(db, customerOpeningBalance: 300m);
        var secondSupplier = await new CreateContactCommandHandler(db, seed.NumberGenerator).Handle(
            new CreateContactCommand(
                seed.OrganizationId, ContactType.Supplier, "Himal Distributors", null, null, null, null, null, 0m),
            CancellationToken.None);

        foreach (var customer in new[] { seed.CustomerId, seed.SecondCustomerId })
        {
            var partlyPaid = await seed.ApproveInvoiceAsync(db, AsOf.AddDays(-60), 1_000m, customer);
            await seed.ApprovePaymentAsync(
                db, AsOf.AddDays(-20), PaymentDirection.Received, customer, [(DocumentType.Invoice, partlyPaid.Id, 350m)]);

            var returned = await seed.ApproveInvoiceAsync(db, AsOf.AddDays(-40), 400m, customer, quantity: 2m);
            await seed.ApproveCreditNoteAsync(db, AsOf.AddDays(-10), 1m, 400m, returned.Id, contactId: customer);

            await seed.ApproveContactJournalVoucherAsync(db, AsOf.AddDays(-15), customer, seed.ArAccountId, 250m, debitContact: true);
        }

        foreach (var supplier in new[] { seed.SupplierId, secondSupplier.Id })
        {
            var asSupplier = seed with { SupplierId = supplier };
            var bill = await asSupplier.ApprovePurchaseBillAsync(db, AsOf.AddDays(-50), 500m, quantity: 4m);
            await asSupplier.ApprovePaymentAsync(
                db, AsOf.AddDays(-4), PaymentDirection.Paid, supplier, [(DocumentType.PurchaseBill, bill.Id, 750m)]);
            await asSupplier.ApproveDebitNoteAsync(db, AsOf.AddDays(-3), 1m, 500m, bill.Id);
            await asSupplier.ApproveContactJournalVoucherAsync(
                db, AsOf.AddDays(-35), supplier, seed.ApAccountId, 400m, debitContact: false);
        }

        await AssertOneContactReadsAsTheWholeAsync(db, seed.OrganizationId, ContactType.Customer, seed.CustomerId, AsOf);
        await AssertOneContactReadsAsTheWholeAsync(db, seed.OrganizationId, ContactType.Customer, seed.SecondCustomerId, AsOf);
        await AssertOneContactReadsAsTheWholeAsync(db, seed.OrganizationId, ContactType.Supplier, seed.SupplierId, AsOf);
        await AssertOneContactReadsAsTheWholeAsync(db, seed.OrganizationId, ContactType.Supplier, secondSupplier.Id, AsOf);
    }

    [Fact]
    public async Task A_till_sales_tenders_change_and_refund_payouts_narrow_to_its_own_customer()
    {
        var till = await PosTestTill.CreateAsync();
        var session = await till.OpenAsync();
        var today = PosTestTill.Today;

        // The walk-in: one sale paid with change, one refunded in part over the counter.
        await till.SellAsync(session.Id, [till.Momo(2), till.Coke(2)], [till.Cash(1000m)], change: 367m);
        var refunded = await till.SellAsync(session.Id, [till.Momo(2), till.Coke(2)], [till.Cash(1000m)], change: 367m);
        await till.RefundAsync(session.Id, refunded.Id, [await till.ReturnAsync(refunded.Id, till.CokeId, 1m)], [till.Cash(68m)]);

        // The named customer: a till sale part on credit and refunded in part (a refund clears what is still owed before
        // it pays anything out, so this one goes back on account), a till sale paid with change and refunded in part over
        // the counter, and an ERP invoice beside them.
        var onCredit = await till.SellAsync(
            session.Id, [till.Momo(2), till.Coke(1)], [till.Card(100m)], contactId: till.Seed.CustomerId);
        await till.RefundAsync(session.Id, onCredit.Id, [await till.ReturnAsync(onCredit.Id, till.MomoId, 1m)], []);
        var paidUp = await till.SellAsync(
            session.Id, [till.Momo(2), till.Coke(2)], [till.Cash(1000m)], change: 367m, contactId: till.Seed.CustomerId);
        await till.RefundAsync(session.Id, paidUp.Id, [await till.ReturnAsync(paidUp.Id, till.CokeId, 1m)], [till.Cash(68m)]);
        await InventoryReportSeed.SellAsync(till.Db, till.Seed, today, 1m, 500m, till.Seed.ProductId, VatRate.ThirteenPercentVat);

        await AssertOneContactReadsAsTheWholeAsync(till.Db, till.OrganizationId, ContactType.Customer, till.Seed.CustomerId, today);
        await AssertOneContactReadsAsTheWholeAsync(till.Db, till.OrganizationId, ContactType.Customer, till.WalkInId, today, expectRows: false);
    }

    private static async Task AssertOneContactReadsAsTheWholeAsync(
        IAppDbContext db, Guid organizationId, ContactType contactType, Guid contactId, DateOnly asOf, bool expectRows = true)
    {
        var handler = new DocumentAgeQueryHandler(db, new FakeCurrentUserService(Guid.NewGuid()));

        var everyone = await handler.Handle(
            new DocumentAgeQuery(organizationId, contactType, asOf.AddYears(-1), asOf, ExportAll: true), CancellationToken.None);
        var one = await handler.Handle(
            new DocumentAgeQuery(organizationId, contactType, asOf.AddYears(-1), asOf, contactId, ExportAll: true),
            CancellationToken.None);

        var expected = everyone.Rows
            .Where(x => x.ContactId == contactId)
            .Select(x => (x.DocumentType, x.DocumentId, x.Amount, x.Paid, x.Balance))
            .OrderBy(x => x.DocumentId)
            .ToList();
        var actual = one.Rows
            .Select(x => (x.DocumentType, x.DocumentId, x.Amount, x.Paid, x.Balance))
            .OrderBy(x => x.DocumentId)
            .ToList();

        Assert.Equal(expected, actual);
        if (expectRows)
        {
            // Meaningful only if something was settled: a narrowing that dropped a settlement shows as a wrong Paid.
            Assert.Contains(actual, x => x.Paid > 0);
        }
        else
        {
            Assert.Empty(actual);
        }
    }
}
