using ErpApp.Application.Accounting.Commands.CreateBankReconciliation;
using ErpApp.Application.Accounting.Commands.DeleteBankReconciliation;
using ErpApp.Application.Accounting.Commands.DeleteBankStatementLines;
using ErpApp.Application.Accounting.Queries.BankReconciliationReport;
using ErpApp.Application.Accounting.Queries.GetBankReconciliation;
using ErpApp.Application.Accounting.Queries.ListBankStatementLines;
using ErpApp.Application.Accounting.Queries.ListBookTransactions;
using ErpApp.Application.Accounting.Reports;
using ErpApp.Application.Common.Exceptions;
using ErpApp.Application.Common.Pagination;
using ErpApp.Application.Common.Persistence;
using ErpApp.Application.Common.Security;
using ErpApp.Application.UnitTests.TestSupport;
using ErpApp.Domain.Accounting;
using ErpApp.Domain.Common;
using FluentValidation;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.UnitTests.Accounting;

/// <summary>
/// Phase 56 — the two-pane matcher, at handler level.
///
/// <para>The seed deliberately mirrors the shape the reference product was driven through on
/// 2026-09-21: one bank account with two receipts of 678 and 113 posted against it, and statement
/// lines of 678, 113 and 791 imported from a file. That is what makes 1:1, 1:2 and 2:2 all
/// expressible on the same data, and what makes the unequal-sum refusal a realistic mistake rather
/// than a contrived one.</para>
/// </summary>
public class BankReconciliationTests
{
    private static readonly Guid OrganizationId = Guid.NewGuid();
    private static readonly Guid OtherOrganizationId = Guid.NewGuid();
    private static readonly Guid ActingUserId = Guid.NewGuid();
    private static readonly DateTimeOffset Now = new(2026, 9, 21, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task One_statement_line_reconciles_against_two_receipts()
    {
        var db = TestAppDbContext.Create();
        var seed = await SeedAsync(db);

        var result = await ReconcileAsync(db, seed.AccountId, [seed.Bank791], [seed.Book678, seed.Book113]);

        Assert.Equal(791m, result.ReconciledAmount);
        Assert.Equal(1, result.StatementLineCount);
        Assert.Equal(2, result.BookTransactionCount);

        // Both sides carry the same id -- the shape read live, and the reason there is no link table.
        var line = await db.BankStatementLines.SingleAsync(x => x.Id == seed.Bank791);
        var books = await db.GlLines.Where(x => x.ReconciliationId == result.Id).ToListAsync();

        Assert.Equal(result.Id, line.ReconciliationId);
        Assert.Equal(2, books.Count);
    }

    [Fact]
    public async Task Two_statement_lines_reconcile_against_two_receipts()
    {
        var db = TestAppDbContext.Create();
        var seed = await SeedAsync(db);

        var result = await ReconcileAsync(
            db, seed.AccountId, [seed.Bank678, seed.Bank113], [seed.Book678, seed.Book113]);

        Assert.Equal(791m, result.ReconciledAmount);

        var ids = await db.BankStatementLines
            .Where(x => x.ReconciliationId == result.Id)
            .Select(x => x.Id)
            .ToListAsync();

        Assert.Equal(2, ids.Count);
    }

    /// <summary>
    /// <b>The invariant, at the boundary where a caller sees it.</b> The reference product answers
    /// its own 400 to exactly this pair (678 against 113, probed live), and this codebase's rule is
    /// that a Domain invariant reached through an endpoint must arrive as a 400 naming a field
    /// rather than a 500 (phase 39). So the handler raises it, and the message carries both totals —
    /// the user's next move is to fix a selection, and they cannot do that without knowing by how
    /// much it is out.
    /// </summary>
    [Fact]
    public async Task An_unbalanced_selection_is_a_400_naming_both_totals()
    {
        var db = TestAppDbContext.Create();
        var seed = await SeedAsync(db);

        var ex = await Assert.ThrowsAsync<ValidationException>(() =>
            ReconcileAsync(db, seed.AccountId, [seed.Bank678], [seed.Book113]));

        Assert.Contains("678", ex.Message);
        Assert.Contains("113", ex.Message);

        // Nothing was written -- the failure is before any mutation.
        Assert.Empty(await db.BankReconciliations.ToListAsync());
        Assert.Null((await db.BankStatementLines.SingleAsync(x => x.Id == seed.Bank678)).ReconciliationId);
    }

    [Fact]
    public async Task An_already_reconciled_row_is_a_409()
    {
        var db = TestAppDbContext.Create();
        var seed = await SeedAsync(db);

        await ReconcileAsync(db, seed.AccountId, [seed.Bank678], [seed.Book678]);

        var ex = await Assert.ThrowsAsync<ConflictException>(() =>
            ReconcileAsync(db, seed.AccountId, [seed.Bank678], [seed.Book678]));

        Assert.Contains("already reconciled", ex.Message);
    }

    /// <summary>
    /// A partial match would reconcile a smaller set than the user ticked, and would then usually
    /// fail the sum rule with a total that makes no sense to them.
    /// </summary>
    [Fact]
    public async Task An_id_that_does_not_resolve_is_a_404_rather_than_a_partial_match()
    {
        var db = TestAppDbContext.Create();
        var seed = await SeedAsync(db);

        await Assert.ThrowsAsync<NotFoundException>(() =>
            ReconcileAsync(db, seed.AccountId, [seed.Bank678, Guid.NewGuid()], [seed.Book678]));

        await Assert.ThrowsAsync<NotFoundException>(() =>
            ReconcileAsync(db, seed.AccountId, [seed.Bank678], [seed.Book678, Guid.NewGuid()]));
    }

    /// <summary>
    /// Seeded on the same tenant, so the assertion is about the <i>account</i> half of the filter
    /// and not about the tenant half, which would hide a bug in it (phase 55's own idiom).
    /// </summary>
    [Fact]
    public async Task Another_account_s_rows_cannot_be_reconciled_into_this_account()
    {
        var db = TestAppDbContext.Create();
        var seed = await SeedAsync(db);

        await Assert.ThrowsAsync<NotFoundException>(() =>
            ReconcileAsync(db, seed.OtherAccountId, [seed.Bank678], [seed.Book678]));
    }

    [Fact]
    public async Task Another_tenant_cannot_reach_these_rows()
    {
        var db = TestAppDbContext.Create();
        var seed = await SeedAsync(db);

        await Assert.ThrowsAsync<NotFoundException>(() =>
            new CreateBankReconciliationCommandHandler(db, new FakeCurrentUser())
                .Handle(
                    new CreateBankReconciliationCommand(
                        OtherOrganizationId, seed.AccountId, [seed.Bank678], [seed.Book678]),
                    CancellationToken.None));
    }

    [Fact]
    public async Task Unreconciling_releases_both_sides_and_removes_the_record()
    {
        var db = TestAppDbContext.Create();
        var seed = await SeedAsync(db);

        var created = await ReconcileAsync(db, seed.AccountId, [seed.Bank791], [seed.Book678, seed.Book113]);

        var result = await new DeleteBankReconciliationCommandHandler(db).Handle(
            new DeleteBankReconciliationCommand(OrganizationId, seed.AccountId, created.Id),
            CancellationToken.None);

        Assert.Equal(1, result.ReleasedStatementLineCount);
        Assert.Equal(2, result.ReleasedBookTransactionCount);

        Assert.Empty(await db.BankReconciliations.ToListAsync());
        Assert.Null((await db.BankStatementLines.SingleAsync(x => x.Id == seed.Bank791)).ReconciliationId);
        Assert.Empty(await db.GlLines.Where(x => x.ReconciliationId != null).ToListAsync());

        // ...and the rows themselves are still there. Unreconciling is not deleting.
        Assert.Equal(3, await db.BankStatementLines.CountAsync(x => x.OrganizationId == OrganizationId));
    }

    [Fact]
    public async Task Unreconciling_something_that_is_not_there_is_a_404()
    {
        var db = TestAppDbContext.Create();
        var seed = await SeedAsync(db);

        await Assert.ThrowsAsync<NotFoundException>(() =>
            new DeleteBankReconciliationCommandHandler(db).Handle(
                new DeleteBankReconciliationCommand(OrganizationId, seed.AccountId, Guid.NewGuid()),
                CancellationToken.None));
    }

    /// <summary>
    /// <b>The guard phase 55 promised and could not write.</b> The reference product deletes a
    /// reconciled line without complaint; we refuse, because the undo here is "undo this import"
    /// over a whole file and letting that dissolve reconciliations made afterwards is an invisible
    /// side effect.
    /// </summary>
    [Fact]
    public async Task A_reconciled_statement_line_cannot_be_deleted()
    {
        var db = TestAppDbContext.Create();
        var seed = await SeedAsync(db);
        await ReconcileAsync(db, seed.AccountId, [seed.Bank678], [seed.Book678]);

        var ex = await Assert.ThrowsAsync<ConflictException>(() =>
            new DeleteBankStatementLinesCommandHandler(db).Handle(
                new DeleteBankStatementLinesCommand(OrganizationId, seed.AccountId, [seed.Bank678], null),
                CancellationToken.None));

        Assert.Contains("Unreconcile them first", ex.Message);
        Assert.Equal(3, await db.BankStatementLines.CountAsync(x => x.OrganizationId == OrganizationId));
    }

    /// <summary>And an undo-this-import that sweeps up one reconciled line refuses the whole run,
    /// rather than deleting the rest and leaving a partial file behind.</summary>
    [Fact]
    public async Task Undoing_an_import_containing_a_reconciled_line_refuses_the_whole_run()
    {
        var db = TestAppDbContext.Create();
        var seed = await SeedAsync(db);
        await ReconcileAsync(db, seed.AccountId, [seed.Bank678], [seed.Book678]);

        await Assert.ThrowsAsync<ConflictException>(() =>
            new DeleteBankStatementLinesCommandHandler(db).Handle(
                new DeleteBankStatementLinesCommand(OrganizationId, seed.AccountId, null, seed.ImportJobId),
                CancellationToken.None));

        Assert.Equal(3, await db.BankStatementLines.CountAsync(x => x.OrganizationId == OrganizationId));
    }

    [Fact]
    public async Task The_detail_drawer_carries_both_sides_and_who_did_it()
    {
        var db = TestAppDbContext.Create();
        var seed = await SeedAsync(db);
        var created = await ReconcileAsync(db, seed.AccountId, [seed.Bank791], [seed.Book678, seed.Book113]);

        var detail = await new GetBankReconciliationQueryHandler(db).Handle(
            new GetBankReconciliationQuery(OrganizationId, seed.AccountId, created.Id),
            CancellationToken.None);

        Assert.Single(detail.StatementLines);
        Assert.Equal(2, detail.BookTransactions.Count);
        Assert.Equal(791m, detail.ReconciledAmount);
        Assert.Equal(ActingUserId, detail.ReconciledByUserId);

        // The book rows carry their source document's code, which is what the join back exists for.
        Assert.All(detail.BookTransactions, x => Assert.Equal(DocumentType.Invoice, x.DocumentType));
    }

    /// <summary>
    /// The matcher's right-hand pane: unreconciled only, and a reconciled row leaves it.
    /// </summary>
    [Fact]
    public async Task The_book_pane_shows_only_what_is_still_unmatched()
    {
        var db = TestAppDbContext.Create();
        var seed = await SeedAsync(db);

        var before = await ListBookAsync(db, seed.AccountId, reconciled: false);
        Assert.Equal(2, before.TotalCount);

        await ReconcileAsync(db, seed.AccountId, [seed.Bank678], [seed.Book678]);

        var after = await ListBookAsync(db, seed.AccountId, reconciled: false);
        Assert.Equal(1, after.TotalCount);

        // ...while the Book Statement screen, which passes no filter, still shows both.
        Assert.Equal(2, (await ListBookAsync(db, seed.AccountId, reconciled: null)).TotalCount);
    }

    /// <summary>
    /// The book feed reads <c>GlLine</c>, so an entry that does not touch this account contributes
    /// nothing — the contra side of every posting is against a different account, and if the reader
    /// were keyed on the document rather than the line it would appear here twice.
    /// </summary>
    [Fact]
    public async Task The_book_pane_shows_one_row_per_posting_against_this_account_only()
    {
        var db = TestAppDbContext.Create();
        var seed = await SeedAsync(db);

        var rows = await ListBookAsync(db, seed.AccountId, reconciled: null);

        Assert.Equal(2, rows.TotalCount);
        Assert.Equal([678m, 113m], rows.Items.Select(x => x.SignedAmount).OrderByDescending(x => x));

        // The contra account is named, which is the Description column.
        Assert.All(rows.Items, x => Assert.Equal("Trade Receivables", x.Description));
    }

    [Fact]
    public async Task The_statement_status_filter_is_the_reconciliation_key()
    {
        var db = TestAppDbContext.Create();
        var seed = await SeedAsync(db);
        var created = await ReconcileAsync(db, seed.AccountId, [seed.Bank678], [seed.Book678]);

        var pending = await ListStatementAsync(db, seed.AccountId, reconciled: false);
        var done = await ListStatementAsync(db, seed.AccountId, reconciled: true);
        var all = await ListStatementAsync(db, seed.AccountId, reconciled: null);

        Assert.Equal(2, pending.TotalCount);
        Assert.Equal(1, done.TotalCount);
        Assert.Equal(3, all.TotalCount);
        Assert.Equal(created.Id, done.Items[0].ReconciliationId);
        Assert.All(pending.Items, x => Assert.Null(x.ReconciliationId));
    }

    /// <summary>
    /// <b>Phase 36's rule, applied.</b> Two readers agree only through one shared reader plus a test
    /// reading both on the same data — patching divergences leaves coincidence. The report's
    /// <i>Unrecognized Transaction</i> figures and the matcher's two panes are the same rows, so
    /// this test drives all four on one dataset and asserts they agree before and after a
    /// reconciliation.
    /// </summary>
    [Fact]
    public async Task The_report_and_the_matcher_cannot_disagree()
    {
        var db = TestAppDbContext.Create();
        var seed = await SeedAsync(db);

        async Task AssertAgreesAsync(string when)
        {
            var report = await ReportAsync(db, seed.AccountId);
            var bankPane = await ListStatementAsync(db, seed.AccountId, reconciled: false);
            var bookPane = await ListBookAsync(db, seed.AccountId, reconciled: false);

            Assert.Equal(bankPane.TotalCount, report.UnreconciledBankCount);
            Assert.Equal(bookPane.TotalCount, report.UnreconciledBookCount);
            Assert.Equal(
                bankPane.Items.Sum(x => x.SignedAmount), report.UnreconciledBankTotal);
            Assert.Equal(
                bookPane.Items.Sum(x => x.SignedAmount), report.UnreconciledBookTotal);

            // And the headline difference is the two independent balances, not a function of how
            // much reconciling has been done -- which is the whole reason a reconciliation posts
            // nothing.
            Assert.Equal(report.BankBalance - report.BookBalance, report.Difference);
            Assert.Equal(1582m, report.BankBalance);
            Assert.Equal(791m, report.BookBalance);
            Assert.Equal(791m, report.Difference);
        }

        await AssertAgreesAsync("before");
        await ReconcileAsync(db, seed.AccountId, [seed.Bank678], [seed.Book678]);
        await AssertAgreesAsync("after");
    }

    /// <summary>
    /// The numbers the live reference product showed on exactly this data, so the report's meaning
    /// is pinned to a reading rather than to this implementation's own arithmetic:
    /// bank 1,582 / book 791 / difference -791 with two unrecognized bank rows totalling 791.
    /// (The sign is the reference product's <i>book less bank</i>; ours reports bank less book and
    /// labels it, because every other figure here is signed from the account's point of view.)
    /// </summary>
    [Fact]
    public async Task The_report_matches_the_figures_read_live()
    {
        var db = TestAppDbContext.Create();
        var seed = await SeedAsync(db);
        await ReconcileAsync(db, seed.AccountId, [seed.Bank791], [seed.Book678, seed.Book113]);

        var report = await ReportAsync(db, seed.AccountId);

        Assert.Equal(1582m, report.BankBalance);
        Assert.Equal(791m, report.BookBalance);
        Assert.Equal(791m, report.Difference);
        Assert.Equal(791m, report.UnreconciledBankTotal);
        Assert.Equal(2, report.UnreconciledBankCount);
        Assert.Equal(0m, report.UnreconciledBookTotal);
        Assert.Equal(0, report.UnreconciledBookCount);
    }

    /// <summary>
    /// The bank side cuts off on the statement line's own Date, so the 791 line -- dated the 17th --
    /// is outside a view taken as of the 16th, while the two lines dated the 16th are inside it.
    /// </summary>
    [Fact]
    public async Task The_report_cuts_off_at_the_as_of_date()
    {
        var db = TestAppDbContext.Create();
        var seed = await SeedAsync(db);

        var early = await ReportAsync(db, seed.AccountId, new DateOnly(2026, 9, 16));

        Assert.Equal(791m, early.BankBalance);
        Assert.Equal(2, early.UnreconciledBankCount);
        Assert.DoesNotContain(early.UnreconciledStatementLines, x => x.Description == "CLAUDE PROBE C");

        // ...and the day after, all three are in view.
        var late = await ReportAsync(db, seed.AccountId, new DateOnly(2026, 9, 17));

        Assert.Equal(1582m, late.BankBalance);
        Assert.Equal(3, late.UnreconciledBankCount);
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public void Both_sides_have_to_be_selected(bool bank, bool book)
    {
        var command = new CreateBankReconciliationCommand(
            OrganizationId, Guid.NewGuid(),
            bank ? [Guid.NewGuid()] : [],
            book ? [Guid.NewGuid()] : []);

        Assert.False(new CreateBankReconciliationCommandValidator().Validate(command).IsValid);
    }

    /// <summary>A repeated id would be counted twice by the sum and once by the update, so a caller
    /// could balance a selection against itself.</summary>
    [Fact]
    public void A_repeated_id_is_refused()
    {
        var id = Guid.NewGuid();

        var command = new CreateBankReconciliationCommand(
            OrganizationId, Guid.NewGuid(), [id, id], [Guid.NewGuid()]);

        Assert.False(new CreateBankReconciliationCommandValidator().Validate(command).IsValid);
    }

    private static Task<CreateBankReconciliationResult> ReconcileAsync(
        IAppDbContext db, Guid accountId, Guid[] bankIds, Guid[] bookIds) =>
        new CreateBankReconciliationCommandHandler(db, new FakeCurrentUser()).Handle(
            new CreateBankReconciliationCommand(OrganizationId, accountId, bankIds, bookIds),
            CancellationToken.None);

    private static Task<PagedResult<BookTransactionDto>> ListBookAsync(
        IAppDbContext db, Guid accountId, bool? reconciled) =>
        new ListBookTransactionsQueryHandler(db).Handle(
            new ListBookTransactionsQuery(OrganizationId, accountId, reconciled, 1, 50),
            CancellationToken.None);

    private static Task<PagedResult<BankStatementLineListItem>> ListStatementAsync(
        IAppDbContext db, Guid accountId, bool? reconciled) =>
        new ListBankStatementLinesQueryHandler(db).Handle(
            new ListBankStatementLinesQuery(
                OrganizationId, accountId, 1, 50, null, null, null, null, reconciled),
            CancellationToken.None);

    private static Task<BankReconciliationReportDto> ReportAsync(
        IAppDbContext db, Guid accountId, DateOnly? asOf = null) =>
        new BankReconciliationReportQueryHandler(db).Handle(
            new BankReconciliationReportQuery(
                OrganizationId, accountId, asOf ?? new DateOnly(2026, 12, 31)),
            CancellationToken.None);

    private sealed record Seeded(
        Guid AccountId,
        Guid OtherAccountId,
        Guid ImportJobId,
        Guid Bank678,
        Guid Bank113,
        Guid Bank791,
        Guid Book678,
        Guid Book113);

    /// <summary>
    /// Cadehi's shape, as it was driven live: one cash account holding two receipts of 678 and 113,
    /// and a statement carrying 678, 113 and 791.
    /// </summary>
    private static async Task<Seeded> SeedAsync(IAppDbContext db)
    {
        var groupId = Guid.NewGuid();

        var bank = Account.Create(
            OrganizationId, "BC0001", "Cash In Hand", AccountRootType.Asset, groupId, AccountKind.Cash);
        var otherBank = Account.Create(
            OrganizationId, "BC0002", "Nabil Bank", AccountRootType.Asset, groupId, AccountKind.Bank);
        var receivable = Account.Create(
            OrganizationId, "1200", "Trade Receivables", AccountRootType.Asset, groupId, AccountKind.Other);

        db.Accounts.AddRange(bank, otherBank, receivable);

        var importJobId = Guid.NewGuid();

        var bank678 = BankStatementLine.Create(
            OrganizationId, bank.Id, new DateOnly(2026, 9, 16), "CLAUDE PROBE A",
            StatementAmount.Deposit(678m), importJobId, Now);
        var bank113 = BankStatementLine.Create(
            OrganizationId, bank.Id, new DateOnly(2026, 9, 16), "CLAUDE PROBE B",
            StatementAmount.Deposit(113m), importJobId, Now.AddMinutes(1));
        var bank791 = BankStatementLine.Create(
            OrganizationId, bank.Id, new DateOnly(2026, 9, 17), "CLAUDE PROBE C",
            StatementAmount.Deposit(791m), importJobId, Now.AddMinutes(2));

        db.BankStatementLines.AddRange(bank678, bank113, bank791);

        // Two separate invoices, so each is its own entry -- which is what a real receipt looks like
        // and what makes "one row per posting against this account" a meaningful assertion.
        var entry678 = GlJournalEntry.Post(
            OrganizationId, DocumentType.Invoice, Guid.NewGuid(),
            [new GlLineInput(bank.Id, 678m, 0m), new GlLineInput(receivable.Id, 0m, 678m)]);
        var entry113 = GlJournalEntry.Post(
            OrganizationId, DocumentType.Invoice, Guid.NewGuid(),
            [new GlLineInput(bank.Id, 113m, 0m), new GlLineInput(receivable.Id, 0m, 113m)]);

        db.GlJournalEntries.AddRange(entry678, entry113);

        await db.SaveChangesAsync();

        return new Seeded(
            bank.Id,
            otherBank.Id,
            importJobId,
            bank678.Id,
            bank113.Id,
            bank791.Id,
            entry678.Lines.Single(x => x.AccountId == bank.Id).Id,
            entry113.Lines.Single(x => x.AccountId == bank.Id).Id);
    }

    private sealed class FakeCurrentUser : ICurrentUserService
    {
        public Guid UserId => ActingUserId;
    }
}
