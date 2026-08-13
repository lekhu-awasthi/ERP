using ErpApp.Application.Accounting;
using ErpApp.Application.Accounting.Commands.ApproveJournalVoucher;
using ErpApp.Application.Accounting.Commands.CreateJournalVoucher;
using ErpApp.Application.Accounting.Posting;
using ErpApp.Application.Accounting.Queries.TrialBalance;
using ErpApp.Application.UnitTests.TestSupport;

namespace ErpApp.Application.UnitTests.Accounting;

public class TrialBalanceQueryHandlerTests
{
    [Fact]
    public async Task Handle_nets_debit_and_credit_onto_each_accounts_natural_side_and_stays_balanced()
    {
        var db = TestAppDbContext.Create();
        var (organizationId, cashAccountId, salesAccountId) = await AccountingTestSeed.SeedTwoAccountsAsync(db);
        await ApproveJournalVoucherAsync(db, organizationId, cashAccountId, salesAccountId, 1000m);

        var handler = new TrialBalanceQueryHandler(db);
        var result = await handler.Handle(
            new TrialBalanceQuery(organizationId, DateOnly.FromDateTime(DateTime.UtcNow)), CancellationToken.None);

        var cashRow = Assert.Single(result.Rows, r => r.AccountId == cashAccountId);
        Assert.Equal(1000m, cashRow.Debit);
        Assert.Equal(0m, cashRow.Credit);

        var salesRow = Assert.Single(result.Rows, r => r.AccountId == salesAccountId);
        Assert.Equal(0m, salesRow.Debit);
        Assert.Equal(1000m, salesRow.Credit);

        Assert.Equal(1000m, result.TotalDebit);
        Assert.Equal(1000m, result.TotalCredit);
        Assert.True(result.IsBalanced);
    }

    [Fact]
    public async Task Handle_excludes_entries_posted_after_the_asOfDate_cutoff()
    {
        var db = TestAppDbContext.Create();
        var (organizationId, cashAccountId, salesAccountId) = await AccountingTestSeed.SeedTwoAccountsAsync(db);
        await ApproveJournalVoucherAsync(db, organizationId, cashAccountId, salesAccountId, 1000m);

        var handler = new TrialBalanceQueryHandler(db);
        var result = await handler.Handle(
            new TrialBalanceQuery(organizationId, DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-1)), CancellationToken.None);

        Assert.All(result.Rows, r => Assert.Equal(0m, r.Debit + r.Credit));
        Assert.Equal(0m, result.TotalDebit);
        Assert.Equal(0m, result.TotalCredit);
        Assert.True(result.IsBalanced);
    }

    [Fact]
    public async Task Handle_lists_every_active_account_even_with_a_zero_balance()
    {
        var db = TestAppDbContext.Create();
        var (organizationId, cashAccountId, salesAccountId) = await AccountingTestSeed.SeedTwoAccountsAsync(db);

        var handler = new TrialBalanceQueryHandler(db);
        var result = await handler.Handle(
            new TrialBalanceQuery(organizationId, DateOnly.FromDateTime(DateTime.UtcNow)), CancellationToken.None);

        Assert.Contains(result.Rows, r => r.AccountId == cashAccountId);
        Assert.Contains(result.Rows, r => r.AccountId == salesAccountId);
        Assert.True(result.IsBalanced);
    }

    private static async Task ApproveJournalVoucherAsync(
        Application.Common.Persistence.IAppDbContext db, Guid organizationId, Guid debitAccountId, Guid creditAccountId, decimal amount)
    {
        var created = await new CreateJournalVoucherCommandHandler(db).Handle(
            new CreateJournalVoucherCommand(
                organizationId, new DateOnly(2026, 1, 1), null,
                [new JournalVoucherLineInput(debitAccountId, amount, 0m), new JournalVoucherLineInput(creditAccountId, 0m, amount)]),
            CancellationToken.None);

        await new ApproveJournalVoucherCommandHandler(
            db, new FakeDocumentNumberGenerator(), new FakeCurrentUserService(Guid.NewGuid()), new JournalVoucherPostingRule())
            .Handle(new ApproveJournalVoucherCommand(organizationId, created.Id), CancellationToken.None);
    }
}
