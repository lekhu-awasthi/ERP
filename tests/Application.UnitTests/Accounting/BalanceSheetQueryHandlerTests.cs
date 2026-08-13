using ErpApp.Application.Accounting;
using ErpApp.Application.Accounting.Commands.ApproveJournalVoucher;
using ErpApp.Application.Accounting.Commands.CreateAccount;
using ErpApp.Application.Accounting.Commands.CreateAccountGroup;
using ErpApp.Application.Accounting.Commands.CreateJournalVoucher;
using ErpApp.Application.Accounting.Posting;
using ErpApp.Application.Accounting.Queries.BalanceSheet;
using ErpApp.Application.Common.Persistence;
using ErpApp.Application.Common.Trees;
using ErpApp.Application.UnitTests.TestSupport;
using ErpApp.Domain.Accounting;

namespace ErpApp.Application.UnitTests.Accounting;

public class BalanceSheetQueryHandlerTests
{
    [Fact]
    public async Task Handle_rolls_up_a_nested_subgroups_accounts_into_its_top_level_group_and_balances()
    {
        var db = TestAppDbContext.Create();
        var organizationId = Guid.NewGuid();
        var numberGenerator = new FakeDocumentNumberGenerator();

        var currentAssets = await new CreateAccountGroupCommandHandler(db).Handle(
            new CreateAccountGroupCommand(organizationId, "Current Assets", AccountRootType.Asset, null), CancellationToken.None);
        var cashAndBank = await new CreateAccountGroupCommandHandler(db).Handle(
            new CreateAccountGroupCommand(organizationId, "Cash & Bank", AccountRootType.Asset, currentAssets.Id), CancellationToken.None);
        var currentLiabilities = await new CreateAccountGroupCommandHandler(db).Handle(
            new CreateAccountGroupCommand(organizationId, "Current Liabilities", AccountRootType.Liability, null), CancellationToken.None);
        var ownersEquity = await new CreateAccountGroupCommandHandler(db).Handle(
            new CreateAccountGroupCommand(organizationId, "Owner's Equity", AccountRootType.Equity, null), CancellationToken.None);
        var salesIncome = await new CreateAccountGroupCommandHandler(db).Handle(
            new CreateAccountGroupCommand(organizationId, "Sales Income", AccountRootType.Income, null), CancellationToken.None);
        var operatingExpenses = await new CreateAccountGroupCommandHandler(db).Handle(
            new CreateAccountGroupCommand(organizationId, "Operating Expenses", AccountRootType.Expense, null), CancellationToken.None);

        // Cash lives two levels down (Current Assets -> Cash & Bank -> Cash) -- proves the
        // full-subtree rollup, not just "accounts directly under a top-level group".
        var cash = await new CreateAccountCommandHandler(db, numberGenerator).Handle(
            new CreateAccountCommand(organizationId, "Cash in Hand", cashAndBank.Id), CancellationToken.None);
        var ap = await new CreateAccountCommandHandler(db, numberGenerator).Handle(
            new CreateAccountCommand(organizationId, "Accounts Payable", currentLiabilities.Id), CancellationToken.None);
        var capital = await new CreateAccountCommandHandler(db, numberGenerator).Handle(
            new CreateAccountCommand(organizationId, "Owner Capital", ownersEquity.Id), CancellationToken.None);
        var sales = await new CreateAccountCommandHandler(db, numberGenerator).Handle(
            new CreateAccountCommand(organizationId, "Sales Revenue", salesIncome.Id), CancellationToken.None);
        var rent = await new CreateAccountCommandHandler(db, numberGenerator).Handle(
            new CreateAccountCommand(organizationId, "Rent Expense", operatingExpenses.Id), CancellationToken.None);

        // Owner injects 10,000 cash capital; sells 3,000 cash; pays 2,000 rent on credit.
        await ApproveJournalVoucherAsync(db, organizationId, cash.Id, capital.Id, 10000m);
        await ApproveJournalVoucherAsync(db, organizationId, cash.Id, sales.Id, 3000m);
        await ApproveJournalVoucherAsync(db, organizationId, rent.Id, ap.Id, 2000m);

        var handler = new BalanceSheetQueryHandler(db, new AccountGroupTreeQuery(db));
        var result = await handler.Handle(
            new BalanceSheetQuery(organizationId, DateOnly.FromDateTime(DateTime.UtcNow)), CancellationToken.None);

        var assetGroup = Assert.Single(result.AssetGroups);
        Assert.Equal(currentAssets.Id, assetGroup.GroupId);
        Assert.Equal(13000m, assetGroup.Balance);

        var liabilityGroup = Assert.Single(result.LiabilityGroups);
        Assert.Equal(2000m, liabilityGroup.Balance);

        Assert.Equal(2, result.EquityGroups.Count);
        var equityGroupRow = Assert.Single(result.EquityGroups, g => g.GroupId == ownersEquity.Id);
        Assert.Equal(10000m, equityGroupRow.Balance);
        var netIncomeRow = Assert.Single(result.EquityGroups, g => g.GroupId == Guid.Empty);
        Assert.Equal(1000m, netIncomeRow.Balance);

        Assert.Equal(1000m, result.NetIncome);
        Assert.Equal(13000m, result.TotalAssets);
        Assert.Equal(2000m, result.TotalLiabilities);
        Assert.Equal(11000m, result.TotalEquity);
        Assert.True(result.IsBalanced);
    }

    [Fact]
    public async Task Handle_reports_zero_everywhere_for_an_organization_with_no_postings()
    {
        var db = TestAppDbContext.Create();
        var (organizationId, _, _) = await AccountingTestSeed.SeedTwoAccountsAsync(db);

        var handler = new BalanceSheetQueryHandler(db, new AccountGroupTreeQuery(db));
        var result = await handler.Handle(
            new BalanceSheetQuery(organizationId, DateOnly.FromDateTime(DateTime.UtcNow)), CancellationToken.None);

        Assert.Equal(0m, result.TotalAssets);
        Assert.Equal(0m, result.TotalLiabilities);
        Assert.Equal(0m, result.TotalEquity);
        Assert.Equal(0m, result.NetIncome);
        Assert.True(result.IsBalanced);
    }

    private static async Task ApproveJournalVoucherAsync(
        IAppDbContext db, Guid organizationId, Guid debitAccountId, Guid creditAccountId, decimal amount)
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
