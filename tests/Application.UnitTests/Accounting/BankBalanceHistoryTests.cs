using ErpApp.Application.Accounting.Queries.BankBalanceHistory;
using ErpApp.Application.Accounting.Queries.BankReconciliationReport;
using ErpApp.Application.Common.Exceptions;
using ErpApp.Application.Common.Persistence;
using ErpApp.Application.UnitTests.TestSupport;
using ErpApp.Domain.Accounting;
using ErpApp.Domain.Common;

namespace ErpApp.Application.UnitTests.Accounting;

/// <summary>
/// Phase 57 — the Balance History chart.
///
/// <para><b>The test that matters is the agreement test.</b> A chart drawn beside two figures has to
/// end at those figures, and phase 26b's rule is that two reports agree only through one shared
/// reader <i>plus a test reading both on the same data</i> — patching a divergence leaves a
/// coincidence. So the first test below runs the chart and the report over one seed and asserts the
/// last point equals the report, rather than asserting each against a number typed into the
/// test.</para>
///
/// <para><b>Everything here is dated relative to <c>UtcNow</c>, never to a fixed date.</b>
/// <c>GlJournalEntry.Post</c> stamps <c>PostedAt</c> at posting time, so a seed pinned to a calendar
/// date would put the book side outside its own window the day after it was written — phase 19's
/// rule, which is exactly about GL reports.</para>
/// </summary>
public class BankBalanceHistoryTests
{
    private static readonly Guid OrganizationId = Guid.NewGuid();
    private static readonly DateTimeOffset Now = new(2026, 9, 21, 10, 0, 0, TimeSpan.Zero);

    /// <summary>The day the book side is stamped with, which is the day this test runs.</summary>
    private static DateOnly Today => DateOnly.FromDateTime(DateTime.UtcNow);

    [Fact]
    public async Task The_last_point_is_the_reconciliation_reports_own_two_balances()
    {
        var db = TestAppDbContext.Create();
        var seed = await SeedAsync(db);

        var report = await ReportAsync(db, seed.BankId, Today);
        var history = await HistoryAsync(db, seed.BankId, Today, 30);

        var last = history.Points[^1];

        Assert.Equal(Today, last.Day);
        Assert.Equal(report.BookBalance, last.BookBalance);
        Assert.Equal(report.BankBalance, last.BankBalance);
        Assert.Equal(report.Difference, last.Difference);

        // ...and the two records really do disagree on this seed, so the agreement being asserted is
        // between the chart and the report rather than between two zeroes.
        Assert.NotEqual(0m, last.Difference);
    }

    /// <summary>
    /// A day on which nothing moved still gets a point, carrying the previous balance forward. A gap
    /// would read as a balance that changed on the days either side of it.
    /// </summary>
    [Fact]
    public async Task Every_day_of_the_window_gets_a_point_including_the_quiet_ones()
    {
        var db = TestAppDbContext.Create();
        var seed = await SeedAsync(db);

        var history = await HistoryAsync(db, seed.BankId, Today, 10);

        Assert.Equal(10, history.Points.Count);
        Assert.Equal(Today.AddDays(-9), history.Points[0].Day);
        Assert.Equal(Today, history.Points[^1].Day);

        // Consecutive, with no day missing and none repeated.
        Assert.Equal(
            Enumerable.Range(0, 10).Select(i => Today.AddDays(-9 + i)),
            history.Points.Select(x => x.Day));

        // Nothing is dated the day before last in the seed, so it holds the previous day's balance
        // rather than dropping to zero -- the difference between a balance series and a movement
        // series.
        var quiet = history.Points.Single(x => x.Day == Today.AddDays(-1));
        var before = history.Points.Single(x => x.Day == Today.AddDays(-2));

        Assert.Equal(before.BankBalance, quiet.BankBalance);
        Assert.Equal(before.BookBalance, quiet.BookBalance);
    }

    /// <summary>
    /// The window's first point already carries everything that happened before it. A chart that
    /// started each series at zero would be a movement chart wearing a balance chart's label.
    /// </summary>
    [Fact]
    public async Task The_first_point_carries_the_balance_from_before_the_window()
    {
        var db = TestAppDbContext.Create();
        var seed = await SeedAsync(db);

        // Two days, so every statement line in the seed is outside the window and all of it has to
        // arrive as the opening balance.
        var history = await HistoryAsync(db, seed.BankId, Today, 2);

        Assert.Equal(2, history.Points.Count);
        Assert.Equal(928m, history.Points[0].BankBalance);
    }

    [Fact]
    public async Task An_account_in_another_organization_is_a_404()
    {
        var db = TestAppDbContext.Create();
        await SeedAsync(db);

        await Assert.ThrowsAsync<NotFoundException>(() =>
            HistoryAsync(db, Guid.NewGuid(), Today, 30));
    }

    private static Task<BankBalanceHistoryDto> HistoryAsync(
        IAppDbContext db, Guid bankAccountId, DateOnly asOf, int days) =>
        new BankBalanceHistoryQueryHandler(db).Handle(
            new BankBalanceHistoryQuery(OrganizationId, bankAccountId, asOf, days), CancellationToken.None);

    private static Task<BankReconciliationReportDto> ReportAsync(
        IAppDbContext db, Guid bankAccountId, DateOnly asOf) =>
        new BankReconciliationReportQueryHandler(db).Handle(
            new BankReconciliationReportQuery(OrganizationId, bankAccountId, asOf), CancellationToken.None);

    private sealed record Seeded(Guid BankId);

    /// <summary>
    /// One account with movement on both sides and on different days, ending in a state the two
    /// records disagree about — which is what the chart exists to show. The bank side totals
    /// 300 + 678 - 50 = 928; the book side is one 678 receipt, so the difference is 250.
    /// </summary>
    private static async Task<Seeded> SeedAsync(IAppDbContext db)
    {
        var groupId = Guid.NewGuid();

        var bank = Account.Create(
            OrganizationId, "BC0001", "Nabil Bank", AccountRootType.Asset, groupId, AccountKind.Bank);
        var receivable = Account.Create(
            OrganizationId, "1200", "Trade Receivables", AccountRootType.Asset, groupId, AccountKind.Other);

        db.Accounts.AddRange(bank, receivable);

        db.BankStatementLines.AddRange(
            BankStatementLine.Create(
                OrganizationId, bank.Id, Today.AddDays(-7), "OPENING DEPOSIT",
                StatementAmount.Deposit(300m), null, Now),
            BankStatementLine.Create(
                OrganizationId, bank.Id, Today.AddDays(-3), "CHEQUE CREDIT",
                StatementAmount.Deposit(678m), null, Now.AddMinutes(1)),
            BankStatementLine.Create(
                OrganizationId, bank.Id, Today.AddDays(-2), "SERVICE CHARGE",
                StatementAmount.Withdrawal(50m), null, Now.AddMinutes(2)));

        // The book side is stamped at Approve time -- PostedAt, not a document date -- which is
        // exactly why the two sides of this chart cut off on different fields (phase 56's Decision
        // F). It therefore lands on today, whenever today is.
        var entry = GlJournalEntry.Post(
            OrganizationId, DocumentType.Invoice, Guid.NewGuid(),
            [new GlLineInput(bank.Id, 678m, 0m), new GlLineInput(receivable.Id, 0m, 678m)]);

        db.GlJournalEntries.Add(entry);

        await db.SaveChangesAsync();

        return new Seeded(bank.Id);
    }
}
