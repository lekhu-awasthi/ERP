using ErpApp.Application.Accounting;
using ErpApp.Application.Accounting.Commands.ApproveJournalVoucher;
using ErpApp.Application.Accounting.Commands.CreateJournalVoucher;
using ErpApp.Application.Accounting.Posting;
using ErpApp.Application.Accounting.Queries.BalanceSheet;
using ErpApp.Application.Accounting.Queries.IncomeStatement;
using ErpApp.Application.Accounting.Queries.TrialBalance;
using ErpApp.Application.Common.Persistence;
using ErpApp.Application.UnitTests.TestSupport;

namespace ErpApp.Application.UnitTests.Accounting;

/// <summary>
/// Phase 26a -- FR-9.1's Compare column on the three financial statements.
///
/// <para>Every posting in these tests lands at <c>DateTimeOffset.UtcNow</c>, because
/// <c>GlJournalEntry.PostedAt</c> is stamped at Approve time and nothing lets a test choose it (the
/// phase-19 lesson: GL report tests bracket UtcNow, they do not use fixed dates). So the windows are
/// built <i>relative to today</i>: a main window in the future with a compare window that brackets
/// now proves the comparison really is a second, earlier window rather than the same figures echoed
/// into extra columns -- which is precisely the bug a naive implementation would have.</para>
/// </summary>
public class FinancialStatementCompareTests
{
    private static DateOnly Today => DateOnly.FromDateTime(DateTime.UtcNow);

    [Fact]
    public async Task TrialBalance_without_compare_leaves_every_compare_field_null()
    {
        var db = TestAppDbContext.Create();
        var (organizationId, cashAccountId, salesAccountId) = await AccountingTestSeed.SeedTwoAccountsAsync(db);
        await PostAsync(db, organizationId, cashAccountId, salesAccountId, 1000m);

        var result = await new TrialBalanceQueryHandler(db).Handle(
            new TrialBalanceQuery(organizationId, Today), CancellationToken.None);

        Assert.Null(result.CompareAsOfDate);
        Assert.Null(result.CompareTotalDebit);
        Assert.Null(result.CompareTotalCredit);
        Assert.All(result.Rows, r =>
        {
            Assert.Null(r.CompareDebit);
            Assert.Null(r.CompareCredit);
        });
    }

    [Fact]
    public async Task TrialBalance_compare_runs_a_second_window_a_year_earlier_and_echoes_the_date()
    {
        var db = TestAppDbContext.Create();
        var (organizationId, cashAccountId, salesAccountId) = await AccountingTestSeed.SeedTwoAccountsAsync(db);
        await PostAsync(db, organizationId, cashAccountId, salesAccountId, 1000m);

        var asOfDate = Today;
        var result = await new TrialBalanceQueryHandler(db).Handle(
            new TrialBalanceQuery(organizationId, asOfDate, Compare: true), CancellationToken.None);

        Assert.Equal(asOfDate.AddYears(-1), result.CompareAsOfDate);
        Assert.Equal(1000m, result.TotalDebit);

        // Nothing existed a year ago, so the compare columns are a real zero -- not a copy of the
        // main window, which is what a Compare that forgot to move its cutoff would produce.
        Assert.Equal(0m, result.CompareTotalDebit);
        Assert.Equal(0m, result.CompareTotalCredit);
        var cashRow = Assert.Single(result.Rows, r => r.AccountId == cashAccountId);
        Assert.Equal(0m, cashRow.CompareDebit);
    }

    [Fact]
    public async Task BalanceSheet_compare_fills_a_compare_balance_on_every_group_row_and_the_totals()
    {
        var db = TestAppDbContext.Create();
        var (organizationId, cashAccountId, salesAccountId) = await AccountingTestSeed.SeedTwoAccountsAsync(db);
        await PostAsync(db, organizationId, cashAccountId, salesAccountId, 1000m);

        var asOfDate = Today;
        var result = await new BalanceSheetQueryHandler(db, new AccountGroupTreeQuery(db)).Handle(
            new BalanceSheetQuery(organizationId, asOfDate, Compare: true), CancellationToken.None);

        Assert.Equal(asOfDate.AddYears(-1), result.CompareAsOfDate);
        Assert.Equal(1000m, result.TotalAssets);
        Assert.Equal(0m, result.CompareTotalAssets);
        Assert.Equal(0m, result.CompareNetIncome);

        // Every group row is present in both windows -- the merge keys on GroupId, and the group
        // hierarchy is not re-derived per window, so the columns line up row for row.
        Assert.All(result.AssetGroups, g => Assert.NotNull(g.CompareBalance));
        Assert.All(result.EquityGroups, g => Assert.NotNull(g.CompareBalance));
    }

    [Fact]
    public async Task BalanceSheet_without_compare_leaves_the_compare_balances_null()
    {
        var db = TestAppDbContext.Create();
        var (organizationId, cashAccountId, salesAccountId) = await AccountingTestSeed.SeedTwoAccountsAsync(db);
        await PostAsync(db, organizationId, cashAccountId, salesAccountId, 1000m);

        var result = await new BalanceSheetQueryHandler(db, new AccountGroupTreeQuery(db)).Handle(
            new BalanceSheetQuery(organizationId, Today), CancellationToken.None);

        Assert.Null(result.CompareAsOfDate);
        Assert.Null(result.CompareTotalAssets);
        Assert.All(result.AssetGroups, g => Assert.Null(g.CompareBalance));
    }

    [Fact]
    public async Task IncomeStatement_compare_reports_the_same_length_preceding_window()
    {
        var db = TestAppDbContext.Create();
        var (organizationId, cashAccountId, salesAccountId) = await AccountingTestSeed.SeedTwoAccountsAsync(db);
        await PostAsync(db, organizationId, cashAccountId, salesAccountId, 1000m);

        // A three-day window starting tomorrow: the compare window is the three days ending today,
        // which brackets UtcNow however close to midnight the test runs.
        var fromDate = Today.AddDays(1);
        var toDate = Today.AddDays(3);

        var result = await new IncomeStatementQueryHandler(db).Handle(
            new IncomeStatementQuery(organizationId, fromDate, toDate, Compare: true), CancellationToken.None);

        Assert.Equal(Today.AddDays(-2), result.CompareFromDate);
        Assert.Equal(Today, result.CompareToDate);

        // Nothing was posted in the (future) main window; the real 1,000 of income sits in the
        // compare window. The row is present in spite of having no main-window movement, which is
        // the union-of-both-windows rule the query documents.
        Assert.Equal(0m, result.TotalIncome);
        Assert.Equal(1000m, result.CompareTotalIncome);
        Assert.Equal(1000m, result.CompareNetIncome);

        var salesRow = Assert.Single(result.IncomeRows, r => r.AccountId == salesAccountId);
        Assert.Equal(0m, salesRow.Amount);
        Assert.Equal(1000m, salesRow.CompareAmount);
    }

    [Fact]
    public async Task IncomeStatement_without_compare_keeps_the_movement_only_row_set()
    {
        var db = TestAppDbContext.Create();
        var (organizationId, cashAccountId, salesAccountId) = await AccountingTestSeed.SeedTwoAccountsAsync(db);
        await PostAsync(db, organizationId, cashAccountId, salesAccountId, 1000m);

        var result = await new IncomeStatementQueryHandler(db).Handle(
            new IncomeStatementQuery(organizationId, Today.AddDays(1), Today.AddDays(3)), CancellationToken.None);

        Assert.Null(result.CompareFromDate);
        Assert.Null(result.CompareNetIncome);
        Assert.Empty(result.IncomeRows);
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
            db, new FakeDocumentNumberGenerator(), new FakeCurrentUserService(Guid.NewGuid()), new JournalVoucherPostingRule())
            .Handle(new ApproveJournalVoucherCommand(organizationId, created.Id), CancellationToken.None);
    }
}
