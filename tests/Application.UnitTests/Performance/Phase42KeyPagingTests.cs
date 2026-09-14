using ErpApp.Application.Accounting;
using ErpApp.Application.Accounting.Cash;
using ErpApp.Application.Accounting.Commands.ApproveJournalVoucher;
using ErpApp.Application.Accounting.Commands.CreateJournalVoucher;
using ErpApp.Application.Accounting.Posting;
using ErpApp.Application.Accounting.Queries.DetailGeneralLedger;
using ErpApp.Application.Common.Pagination;
using ErpApp.Application.Common.Persistence;
using ErpApp.Application.Contacts.Commands.CreateContact;
using ErpApp.Application.Contacts.Queries.ListContacts;
using ErpApp.Application.UnitTests.TestSupport;
using ErpApp.Domain.Contacts;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.UnitTests.Performance;

/// <summary>
/// Phase 42. Every change this phase made is a change to <i>how</i> rows are fetched, so the thing
/// worth asserting is that the answer did not move. Each test here reads the same data through both
/// shapes -- the one that was there before and the one that replaced it -- and requires them to be
/// equal, rather than pinning the new shape's output as a fixture (which would pass just as happily
/// if the conversion were wrong).
///
/// <para>These tests say nothing about speed, and cannot: the InMemory provider has no indexes, no
/// query plans and no OPENJSON, which is the whole substance of what changed. The numbers live in
/// <c>tools/scale/</c> and in the measurement tables of <c>docs/phase-42-status.md</c>; what belongs
/// in a test suite is the correctness half.</para>
/// </summary>
public class Phase42KeyPagingTests
{
    private static DateOnly Today => DateOnly.FromDateTime(DateTime.UtcNow);
    private static DateOnly From => Today.AddDays(-1);
    private static DateOnly To => Today.AddDays(1);

    // ---- The list helper: the two shapes, over the same query -----------------------------------

    [Theory]
    [InlineData(1, 5)]   // first page
    [InlineData(2, 5)]   // a middle page
    [InlineData(5, 5)]   // the last page -- the one whose cost this phase was about
    [InlineData(6, 5)]   // one past the end
    [InlineData(1, 50)]  // a page larger than the set
    public async Task The_key_paged_shape_returns_exactly_what_the_row_paged_shape_returned(int page, int pageSize)
    {
        var db = TestAppDbContext.Create();
        var organizationId = Guid.NewGuid();
        await SeedContactsAsync(db, organizationId, 23);

        var filtered = db.Contacts.Where(x => x.OrganizationId == organizationId);

        var before = await filtered.OrderBy(x => x.Name).ToPagedResultAsync(page, pageSize, CancellationToken.None);
        var after = await filtered.ToKeyPagedResultAsync(
            x => x.Id, q => q.OrderBy(x => x.Name), page, pageSize, CancellationToken.None);

        Assert.Equal(before.TotalCount, after.TotalCount);
        Assert.Equal(before.Page, after.Page);
        Assert.Equal(before.PageSize, after.PageSize);
        Assert.Equal(before.Items.Select(x => x.Id), after.Items.Select(x => x.Id));
    }

    [Fact]
    public async Task A_filter_matching_nothing_returns_the_same_empty_page_and_asks_for_no_rows()
    {
        // The empty short-circuit is where most of the search fix came from: a count of zero is a
        // complete answer, so the page query is never issued. The observable half of that is simply
        // that the result is identical to the old shape's.
        var db = TestAppDbContext.Create();
        var organizationId = Guid.NewGuid();
        await SeedContactsAsync(db, organizationId, 12);

        // "ZZQQXX" is the harness's own no-match term (tools/scale/run-measurement.sh). Matching is
        // case-sensitive on InMemory and case-insensitive on SQL Server, so a term that matches
        // nothing is the one term that means the same thing on both (phase-34b).
        var filtered = db.Contacts.Where(x => x.OrganizationId == organizationId && x.Name.Contains("ZZQQXX"));

        var before = await filtered.OrderBy(x => x.Name).ToPagedResultAsync(1, 50, CancellationToken.None);
        var after = await filtered.ToKeyPagedResultAsync(
            x => x.Id, q => q.OrderBy(x => x.Name), 1, 50, CancellationToken.None);

        Assert.Equal(0, before.TotalCount);
        Assert.Equal(0, after.TotalCount);
        Assert.Empty(after.Items);
    }

    [Fact]
    public async Task Another_organizations_rows_stay_out_of_the_page()
    {
        // Phase-35b's rule, asserted rather than assumed: the helper composes onto the caller's
        // query and never rebuilds one, so it cannot lose the OrganizationId filter -- but a helper
        // that fetched by key alone would, and that is exactly the mistake worth a test.
        var db = TestAppDbContext.Create();
        var mine = Guid.NewGuid();
        var theirs = Guid.NewGuid();
        await SeedContactsAsync(db, mine, 4);
        await SeedContactsAsync(db, theirs, 4);

        var result = await db.Contacts.Where(x => x.OrganizationId == mine).ToKeyPagedResultAsync(
            x => x.Id, q => q.OrderBy(x => x.Name), 1, 50, CancellationToken.None);

        Assert.Equal(4, result.TotalCount);
        Assert.All(result.Items, c => Assert.Equal(mine, c.OrganizationId));
    }

    [Fact]
    public async Task The_list_handler_pages_the_same_contacts_it_paged_before()
    {
        // The helper proven above reaches the screens through the handlers, so one handler is read
        // end to end as well: the sweep converted sixteen of them with one scripted edit, and a
        // scripted edit is exactly the kind that compiles while meaning something else.
        var db = TestAppDbContext.Create();
        var organizationId = Guid.NewGuid();
        await SeedContactsAsync(db, organizationId, 7);

        var handler = new ListContactsQueryHandler(db);
        var page1 = await handler.Handle(new ListContactsQuery(organizationId, Type: null, Page: 1, PageSize: 3), CancellationToken.None);
        var page3 = await handler.Handle(new ListContactsQuery(organizationId, Type: null, Page: 3, PageSize: 3), CancellationToken.None);

        Assert.Equal(7, page1.TotalCount);
        Assert.Equal(3, page1.Items.Count);
        Assert.Single(page3.Items);

        var byName = await db.Contacts.Where(x => x.OrganizationId == organizationId)
            .OrderBy(x => x.Name).Select(x => x.Id).ToListAsync(CancellationToken.None);
        Assert.Equal(byName.Take(3), page1.Items.Select(x => x.Id));
        Assert.Equal(byName.Skip(6), page3.Items.Select(x => x.Id));
    }

    // ---- Detail General Ledger: the row-paged shape against the whole report --------------------

    [Fact]
    public async Task Detail_general_ledger_paged_by_row_reassembles_into_the_unpaged_report()
    {
        // The conversion's whole risk is here. Paging by row means a section can arrive split, and
        // a split section has to carry on from where the previous page stopped -- so the test walks
        // every page at pageSize 1 and requires the concatenation to be the report ExportAll
        // returns: the same postings, in the same order, with the same running balances.
        var db = TestAppDbContext.Create();
        var (organizationId, cashAccountId, salesAccountId) = await AccountingTestSeed.SeedTwoAccountsAsync(db);
        await PostAsync(db, organizationId, cashAccountId, salesAccountId, 1000m);
        await PostAsync(db, organizationId, cashAccountId, salesAccountId, 250m);
        await PostAsync(db, organizationId, salesAccountId, cashAccountId, 75m);

        var handler = new DetailGeneralLedgerQueryHandler(db, new FakeCurrentUserService(Guid.NewGuid()));

        var whole = await handler.Handle(
            new DetailGeneralLedgerQuery(organizationId, From, To, ExportAll: true), CancellationToken.None);

        var wholeRows = whole.Items.SelectMany(a => a.Rows.Select(r => (a.AccountId, r.Debit, r.Credit, r.Balance, r.BalanceType))).ToList();
        Assert.Equal(6, wholeRows.Count);           // three vouchers, two lines each
        Assert.Equal(6, whole.TotalCount);          // TotalCount is postings now, not accounts

        var walked = new List<(Guid, decimal, decimal, decimal, string)>();
        for (var page = 1; page <= whole.TotalCount; page++)
        {
            var result = await handler.Handle(
                new DetailGeneralLedgerQuery(organizationId, From, To, Page: page, PageSize: 1), CancellationToken.None);

            Assert.Equal(whole.TotalCount, result.TotalCount);
            var section = Assert.Single(result.Items);
            var row = Assert.Single(section.Rows);
            walked.Add((section.AccountId, row.Debit, row.Credit, row.Balance, row.BalanceType));

            // The section's own figures are facts about the account over the period, so a partial
            // section prints the same Closing Balance as a whole one.
            var sameAccount = Assert.Single(whole.Items, a => a.AccountId == section.AccountId);
            Assert.Equal(sameAccount.OpeningBalance, section.OpeningBalance);
            Assert.Equal(sameAccount.PeriodDebit, section.PeriodDebit);
            Assert.Equal(sameAccount.PeriodCredit, section.PeriodCredit);
            Assert.Equal(sameAccount.ClosingBalance, section.ClosingBalance);
            Assert.Equal(sameAccount.ClosingBalanceType, section.ClosingBalanceType);

            // And the boundary is disclosed rather than left for the reader to infer.
            Assert.Equal(sameAccount.Rows.Count, section.RowsBefore + section.Rows.Count + section.RowsAfter);
        }

        Assert.Equal(wholeRows, walked);
    }

    [Fact]
    public async Task A_page_past_the_end_of_the_detail_general_ledger_is_empty_and_still_counts_the_rows()
    {
        var db = TestAppDbContext.Create();
        var (organizationId, cashAccountId, salesAccountId) = await AccountingTestSeed.SeedTwoAccountsAsync(db);
        await PostAsync(db, organizationId, cashAccountId, salesAccountId, 1000m);

        var result = await new DetailGeneralLedgerQueryHandler(db, new FakeCurrentUserService(Guid.NewGuid())).Handle(
            new DetailGeneralLedgerQuery(organizationId, From, To, Page: 9, PageSize: 50), CancellationToken.None);

        Assert.Empty(result.Items);
        Assert.Equal(2, result.TotalCount);
    }

    // ---- Seeding ---------------------------------------------------------------------------------

    private static async Task SeedContactsAsync(IAppDbContext db, Guid organizationId, int count)
    {
        for (var i = 0; i < count; i++)
        {
            await new CreateContactCommandHandler(db, new FakeDocumentNumberGenerator()).Handle(
                new CreateContactCommand(
                    organizationId, ContactType.Customer, $"Contact {i:D3}",
                    Address: null, Pan: null, Phone: null, Email: null, GroupId: null, OpeningBalance: 0m),
                CancellationToken.None);
        }
    }

    private static async Task PostAsync(
        IAppDbContext db, Guid organizationId, Guid debitAccountId, Guid creditAccountId, decimal amount)
    {
        var created = await new CreateJournalVoucherCommandHandler(db).Handle(
            new CreateJournalVoucherCommand(
                organizationId, DateOnly.FromDateTime(DateTime.UtcNow), null,
                [new JournalVoucherLineInput(debitAccountId, amount, 0m), new JournalVoucherLineInput(creditAccountId, 0m, amount)]),
            CancellationToken.None);

        await new ApproveJournalVoucherCommandHandler(
            db, new FakeDocumentNumberGenerator(), new FakeCurrentUserService(Guid.NewGuid()),
            new JournalVoucherPostingRule(), new GlCashBalancePolicy(db))
            .Handle(new ApproveJournalVoucherCommand(organizationId, created.Id), CancellationToken.None);
    }
}
