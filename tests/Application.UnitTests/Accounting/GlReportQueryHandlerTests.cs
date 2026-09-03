using ErpApp.Application.Accounting;
using ErpApp.Application.Accounting.Commands.ApproveJournalVoucher;
using ErpApp.Application.Accounting.Commands.CreateAccount;
using ErpApp.Application.Accounting.Commands.CreateAccountGroup;
using ErpApp.Application.Accounting.Commands.CreateJournalVoucher;
using ErpApp.Application.Accounting.Posting;
using ErpApp.Application.Accounting.Queries.DetailGeneralLedger;
using ErpApp.Application.Accounting.Queries.GeneralLedgerMaster;
using ErpApp.Application.Accounting.Queries.GeneralLedgerSummary;
using ErpApp.Application.Accounting.Queries.JournalReport;
using ErpApp.Application.Accounting.Reports;
using ErpApp.Application.Common.Persistence;
using ErpApp.Application.UnitTests.TestSupport;
using ErpApp.Domain.Accounting;
using ErpApp.Domain.Common;

namespace ErpApp.Application.UnitTests.Accounting;

/// <summary>
/// Phase 26a -- the four GL reports the catalog was missing, whose shapes were read live on
/// 2026-09-02 (see docs/phase-26a-status.md).
///
/// <para>Postings land at <c>DateTimeOffset.UtcNow</c>, so every window here brackets today rather
/// than naming a fixed date -- the phase-19 rule.</para>
/// </summary>
public class GlReportQueryHandlerTests
{
    private static DateOnly Today => DateOnly.FromDateTime(DateTime.UtcNow);
    private static DateOnly From => Today.AddDays(-1);
    private static DateOnly To => Today.AddDays(1);

    [Fact]
    public async Task JournalReport_groups_lines_under_their_own_document_and_balances_each_block()
    {
        var db = TestAppDbContext.Create();
        var (organizationId, cashAccountId, salesAccountId) = await AccountingTestSeed.SeedTwoAccountsAsync(db);
        await PostAsync(db, organizationId, cashAccountId, salesAccountId, 1000m);
        await PostAsync(db, organizationId, cashAccountId, salesAccountId, 250m);

        var result = await new JournalReportQueryHandler(db).Handle(
            new JournalReportQuery(organizationId, From, To), CancellationToken.None);

        // Two documents, two blocks -- the pager counts documents, not lines.
        Assert.Equal(2, result.TotalCount);
        Assert.Equal(2, result.Items.Count);
        Assert.All(result.Items, entry =>
        {
            Assert.Equal(2, entry.Lines.Count);
            Assert.Equal(entry.TotalDebit, entry.TotalCredit);
            Assert.Equal(DocumentType.JournalVoucher, entry.DocumentType);
            Assert.NotNull(entry.DocumentCode);
        });
        Assert.Contains(result.Items, e => e.TotalDebit == 1000m);
        Assert.Contains(result.Items, e => e.TotalDebit == 250m);
    }

    [Fact]
    public async Task JournalReport_filters_to_one_transaction_type()
    {
        var db = TestAppDbContext.Create();
        var (organizationId, cashAccountId, salesAccountId) = await AccountingTestSeed.SeedTwoAccountsAsync(db);
        await PostAsync(db, organizationId, cashAccountId, salesAccountId, 1000m);

        var matching = await new JournalReportQueryHandler(db).Handle(
            new JournalReportQuery(organizationId, From, To, DocumentType.JournalVoucher), CancellationToken.None);
        var other = await new JournalReportQueryHandler(db).Handle(
            new JournalReportQuery(organizationId, From, To, DocumentType.Invoice), CancellationToken.None);

        Assert.Single(matching.Items);
        Assert.Empty(other.Items);
    }

    [Fact]
    public async Task GeneralLedgerSummary_closes_at_opening_plus_movement_and_marks_each_side()
    {
        var db = TestAppDbContext.Create();
        var (organizationId, cashAccountId, salesAccountId) = await AccountingTestSeed.SeedTwoAccountsAsync(db);
        await PostAsync(db, organizationId, cashAccountId, salesAccountId, 1000m);

        var result = await new GeneralLedgerSummaryQueryHandler(db, new AccountGroupTreeQuery(db)).Handle(
            new GeneralLedgerSummaryQuery(organizationId, From, To), CancellationToken.None);

        var cash = Assert.Single(result.Items, r => r.AccountId == cashAccountId);
        Assert.Equal(0m, cash.OpeningBalance);
        Assert.Equal(1000m, cash.TransactionDebit);
        Assert.Equal(0m, cash.TransactionCredit);
        Assert.Equal(1000m, cash.ClosingBalance);
        Assert.Equal(GlBalanceMarker.Debit, cash.ClosingBalanceType);

        // The Income account nets to a credit, so it is reported as a magnitude marked CR -- never
        // as a negative number.
        var sales = Assert.Single(result.Items, r => r.AccountId == salesAccountId);
        Assert.Equal(1000m, sales.ClosingBalance);
        Assert.Equal(GlBalanceMarker.Credit, sales.ClosingBalanceType);

        // Closing is exactly opening plus movement for every row, which is the report's own claim.
        Assert.All(result.Items, r =>
        {
            var opening = r.OpeningBalanceType == GlBalanceMarker.Debit ? r.OpeningBalance : -r.OpeningBalance;
            var closing = r.ClosingBalanceType == GlBalanceMarker.Debit ? r.ClosingBalance : -r.ClosingBalance;
            Assert.Equal(closing, opening + r.TransactionDebit - r.TransactionCredit);
        });
    }

    [Fact]
    public async Task GeneralLedgerSummary_lists_an_account_that_never_moved()
    {
        var db = TestAppDbContext.Create();
        var (organizationId, _, _) = await AccountingTestSeed.SeedTwoAccountsAsync(db);

        var result = await new GeneralLedgerSummaryQueryHandler(db, new AccountGroupTreeQuery(db)).Handle(
            new GeneralLedgerSummaryQuery(organizationId, From, To), CancellationToken.None);

        Assert.Equal(2, result.TotalCount);
        Assert.All(result.Items, r => Assert.Equal(0m, r.TransactionDebit + r.TransactionCredit));
    }

    [Fact]
    public async Task GeneralLedgerSummary_group_filter_includes_the_groups_descendants()
    {
        var db = TestAppDbContext.Create();
        var organizationId = Guid.NewGuid();
        var numberGenerator = new FakeDocumentNumberGenerator();

        var parent = await new CreateAccountGroupCommandHandler(db).Handle(
            new CreateAccountGroupCommand(organizationId, "Current Assets", AccountRootType.Asset, null), CancellationToken.None);
        var child = await new CreateAccountGroupCommandHandler(db).Handle(
            new CreateAccountGroupCommand(organizationId, "Cash and Bank", AccountRootType.Asset, parent.Id), CancellationToken.None);
        var income = await new CreateAccountGroupCommandHandler(db).Handle(
            new CreateAccountGroupCommand(organizationId, "Sales Income", AccountRootType.Income, null), CancellationToken.None);

        var cash = await new CreateAccountCommandHandler(db, numberGenerator).Handle(
            new CreateAccountCommand(organizationId, "Cash in Hand", child.Id), CancellationToken.None);
        await new CreateAccountCommandHandler(db, numberGenerator).Handle(
            new CreateAccountCommand(organizationId, "Sales Revenue", income.Id), CancellationToken.None);

        var result = await new GeneralLedgerSummaryQueryHandler(db, new AccountGroupTreeQuery(db)).Handle(
            new GeneralLedgerSummaryQuery(organizationId, From, To, GroupId: parent.Id), CancellationToken.None);

        // The account sits in a *sub*group of the filtered group, and must still appear.
        var row = Assert.Single(result.Items);
        Assert.Equal(cash.Id, row.AccountId);
        Assert.Equal("Cash and Bank", row.ParentGroupName);
        Assert.Equal("Current Assets", row.GroupTypeName);
    }

    [Fact]
    public async Task DetailGeneralLedger_runs_a_balance_forward_and_closes_on_the_period_totals()
    {
        var db = TestAppDbContext.Create();
        var (organizationId, cashAccountId, salesAccountId) = await AccountingTestSeed.SeedTwoAccountsAsync(db);
        await PostAsync(db, organizationId, cashAccountId, salesAccountId, 600m);
        await PostAsync(db, organizationId, salesAccountId, cashAccountId, 100m);

        var result = await new DetailGeneralLedgerQueryHandler(db).Handle(
            new DetailGeneralLedgerQuery(organizationId, From, To, AccountId: cashAccountId), CancellationToken.None);

        var section = Assert.Single(result.Items);
        Assert.Equal(cashAccountId, section.AccountId);
        Assert.Equal(0m, section.OpeningBalance);
        Assert.Equal(2, section.Rows.Count);

        // 600 debit then 100 credit -> a 500 debit balance, and the Closing row's Debit/Credit cells
        // carry the period totals, not the last row's own movement.
        Assert.Equal(600m, section.PeriodDebit);
        Assert.Equal(100m, section.PeriodCredit);
        Assert.Equal(500m, section.ClosingBalance);
        Assert.Equal(GlBalanceMarker.Debit, section.ClosingBalanceType);

        Assert.Equal(600m, section.Rows[0].Balance);
        Assert.Equal(500m, section.Rows[1].Balance);

        // The Description column names the other side of each posting.
        Assert.All(section.Rows, r => Assert.Equal("Sales Revenue", r.Description));
    }

    [Fact]
    public async Task DetailGeneralLedger_omits_an_account_with_no_opening_balance_and_no_postings()
    {
        var db = TestAppDbContext.Create();
        var (organizationId, cashAccountId, salesAccountId) = await AccountingTestSeed.SeedTwoAccountsAsync(db);
        var numberGenerator = new FakeDocumentNumberGenerator();
        var group = await new CreateAccountGroupCommandHandler(db).Handle(
            new CreateAccountGroupCommand(organizationId, "Other Expenses", AccountRootType.Expense, null), CancellationToken.None);
        var untouched = await new CreateAccountCommandHandler(db, numberGenerator).Handle(
            new CreateAccountCommand(organizationId, "Rent Expense", group.Id), CancellationToken.None);
        await PostAsync(db, organizationId, cashAccountId, salesAccountId, 600m);

        var result = await new DetailGeneralLedgerQueryHandler(db).Handle(
            new DetailGeneralLedgerQuery(organizationId, From, To), CancellationToken.None);

        Assert.Equal(2, result.TotalCount);
        Assert.DoesNotContain(result.Items, s => s.AccountId == untouched.Id);
    }

    [Fact]
    public async Task GeneralLedgerMaster_returns_one_row_per_posted_line_with_its_classification()
    {
        var db = TestAppDbContext.Create();
        var (organizationId, cashAccountId, salesAccountId) = await AccountingTestSeed.SeedTwoAccountsAsync(db);
        await PostAsync(db, organizationId, cashAccountId, salesAccountId, 1000m);

        var result = await new GeneralLedgerMasterQueryHandler(db).Handle(
            new GeneralLedgerMasterQuery(organizationId, From, To), CancellationToken.None);

        Assert.Equal(2, result.TotalCount);

        var cashRow = Assert.Single(result.Items, r => r.AccountId == cashAccountId);
        Assert.Equal(1000m, cashRow.Debit);
        Assert.Equal(0m, cashRow.Credit);
        Assert.Equal("Cash in Hand", cashRow.AccountName);
        Assert.Equal("Current Assets", cashRow.ParentGroupName);
        Assert.Equal("Current Assets", cashRow.GroupTypeName);
        Assert.Equal(AccountRootType.Asset, cashRow.RootType);
        Assert.Equal(DocumentType.JournalVoucher, cashRow.DocumentType);
        Assert.NotNull(cashRow.DocumentCode);

        // The sheet is balanced by construction, which is why it carries no total row.
        Assert.Equal(result.Items.Sum(r => r.Debit), result.Items.Sum(r => r.Credit));
    }

    [Fact]
    public async Task GeneralLedgerMaster_excludes_postings_outside_the_period()
    {
        var db = TestAppDbContext.Create();
        var (organizationId, cashAccountId, salesAccountId) = await AccountingTestSeed.SeedTwoAccountsAsync(db);
        await PostAsync(db, organizationId, cashAccountId, salesAccountId, 1000m);

        var result = await new GeneralLedgerMasterQueryHandler(db).Handle(
            new GeneralLedgerMasterQuery(organizationId, Today.AddDays(-10), Today.AddDays(-5)), CancellationToken.None);

        Assert.Empty(result.Items);
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
