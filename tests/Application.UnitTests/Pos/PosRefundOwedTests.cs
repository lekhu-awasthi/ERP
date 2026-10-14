using ErpApp.Application.Contacts.Queries.DocumentAge;
using ErpApp.Application.Pos.Queries.GetPosRefundableSale;
using ErpApp.Application.UnitTests.TestSupport;
using ErpApp.Domain.Contacts;

namespace ErpApp.Application.UnitTests.Pos;

/// <summary>
/// Phase 91 -- a till refund reads what its sale is still owed through the sale itself, not through its customer, who
/// at a counter is the walk-in owning every sale (tools/scale/probe-phase91-*.csv: 56,566 logical reads per refund
/// screen before). The narrowing must not change the answer: the refund screen's figure for each sale is held to the
/// balance Invoice Age gives that sale, which reads the same reader organization-wide, on a customer with tenders,
/// credit, a refund and a fully paid sale.
/// </summary>
public class PosRefundOwedTests
{
    [Fact]
    public async Task The_refund_screens_owed_is_the_balance_invoice_age_gives_the_same_sale()
    {
        var till = await PosTestTill.CreateAsync();
        var session = await till.OpenAsync();
        var customer = till.Seed.CustomerId;

        // 633 on credit with 300 paid; 316 on credit with nothing paid, then a coke of it refunded; 68 paid in full.
        var partPaid = await till.SellAsync(session.Id, [till.Momo(2), till.Coke(2)], [till.Cash(300m)], contactId: customer);
        var onCredit = await till.SellAsync(session.Id, [till.Momo(1), till.Coke(1)], [], contactId: customer);
        var paid = await till.SellAsync(session.Id, [till.Coke(1)], [till.Card(68m)], contactId: customer);
        await till.RefundAsync(session.Id, onCredit.Id, [await till.ReturnAsync(onCredit.Id, till.CokeId, 1m)], []);

        var age = await new DocumentAgeQueryHandler(till.Db, new FakeCurrentUserService(Guid.NewGuid())).Handle(
            new DocumentAgeQuery(
                till.OrganizationId, ContactType.Customer, PosTestTill.Today.AddYears(-1), PosTestTill.Today, PageSize: 100),
            CancellationToken.None);

        foreach (var sale in new[] { partPaid.Id, onCredit.Id, paid.Id })
        {
            var screen = await new GetPosRefundableSaleQueryHandler(till.Db, till.CurrentUser()).Handle(
                new GetPosRefundableSaleQuery(till.OrganizationId, till.Location.Id, sale), CancellationToken.None);

            Assert.Equal(age.Rows.Where(x => x.DocumentId == sale).Sum(x => x.Balance), screen.Owed);
        }

        // The figures themselves, so a reader wrong in both reads cannot agree with itself unseen.
        Assert.Equal(333m, age.Rows.Single(x => x.DocumentId == partPaid.Id).Balance);
        Assert.DoesNotContain(age.Rows, x => x.DocumentId == paid.Id);
    }
}
