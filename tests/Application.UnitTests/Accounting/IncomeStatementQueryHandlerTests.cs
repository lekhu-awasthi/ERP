using ErpApp.Application.Accounting;
using ErpApp.Application.Accounting.Commands.ApproveJournalVoucher;
using ErpApp.Application.Accounting.Commands.CreateAccount;
using ErpApp.Application.Accounting.Commands.CreateAccountGroup;
using ErpApp.Application.Accounting.Commands.CreateJournalVoucher;
using ErpApp.Application.Accounting.Posting;
using ErpApp.Application.Accounting.Queries.IncomeStatement;
using ErpApp.Application.Common.Persistence;
using ErpApp.Application.UnitTests.TestSupport;
using ErpApp.Domain.Accounting;

namespace ErpApp.Application.UnitTests.Accounting;

public class IncomeStatementQueryHandlerTests
{
    [Fact]
    public async Task Handle_nets_income_and_expense_and_computes_net_income_within_the_date_range()
    {
        var db = TestAppDbContext.Create();
        var (organizationId, cashAccountId, salesAccountId, apAccountId, rentExpenseAccountId) = await SeedAccountsAsync(db);

        await ApproveJournalVoucherAsync(db, organizationId, cashAccountId, salesAccountId, 3000m);
        await ApproveJournalVoucherAsync(db, organizationId, rentExpenseAccountId, apAccountId, 2000m);

        var handler = new IncomeStatementQueryHandler(db);
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var result = await handler.Handle(
            new IncomeStatementQuery(organizationId, today.AddDays(-1), today.AddDays(1)), CancellationToken.None);

        var incomeRow = Assert.Single(result.IncomeRows);
        Assert.Equal(salesAccountId, incomeRow.AccountId);
        Assert.Equal(3000m, incomeRow.Amount);

        var expenseRow = Assert.Single(result.ExpenseRows);
        Assert.Equal(rentExpenseAccountId, expenseRow.AccountId);
        Assert.Equal(2000m, expenseRow.Amount);

        Assert.Equal(3000m, result.TotalIncome);
        Assert.Equal(2000m, result.TotalExpense);
        Assert.Equal(1000m, result.NetIncome);
    }

    [Fact]
    public async Task Handle_excludes_entries_posted_outside_the_date_range()
    {
        var db = TestAppDbContext.Create();
        var (organizationId, cashAccountId, salesAccountId, _, _) = await SeedAccountsAsync(db);
        await ApproveJournalVoucherAsync(db, organizationId, cashAccountId, salesAccountId, 3000m);

        var handler = new IncomeStatementQueryHandler(db);
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var result = await handler.Handle(
            new IncomeStatementQuery(organizationId, today.AddDays(-10), today.AddDays(-2)), CancellationToken.None);

        Assert.Empty(result.IncomeRows);
        Assert.Empty(result.ExpenseRows);
        Assert.Equal(0m, result.TotalIncome);
        Assert.Equal(0m, result.TotalExpense);
        Assert.Equal(0m, result.NetIncome);
    }

    private static async Task<(Guid OrganizationId, Guid CashAccountId, Guid SalesAccountId, Guid ApAccountId, Guid RentExpenseAccountId)>
        SeedAccountsAsync(IAppDbContext db)
    {
        var organizationId = Guid.NewGuid();
        var numberGenerator = new FakeDocumentNumberGenerator();

        var assetGroup = await new CreateAccountGroupCommandHandler(db).Handle(
            new CreateAccountGroupCommand(organizationId, "Current Assets", AccountRootType.Asset, null), CancellationToken.None);
        var liabilityGroup = await new CreateAccountGroupCommandHandler(db).Handle(
            new CreateAccountGroupCommand(organizationId, "Current Liabilities", AccountRootType.Liability, null), CancellationToken.None);
        var incomeGroup = await new CreateAccountGroupCommandHandler(db).Handle(
            new CreateAccountGroupCommand(organizationId, "Sales Income", AccountRootType.Income, null), CancellationToken.None);
        var expenseGroup = await new CreateAccountGroupCommandHandler(db).Handle(
            new CreateAccountGroupCommand(organizationId, "Operating Expenses", AccountRootType.Expense, null), CancellationToken.None);

        var cash = await new CreateAccountCommandHandler(db, numberGenerator).Handle(
            new CreateAccountCommand(organizationId, "Cash in Hand", assetGroup.Id), CancellationToken.None);
        var sales = await new CreateAccountCommandHandler(db, numberGenerator).Handle(
            new CreateAccountCommand(organizationId, "Sales Revenue", incomeGroup.Id), CancellationToken.None);
        var ap = await new CreateAccountCommandHandler(db, numberGenerator).Handle(
            new CreateAccountCommand(organizationId, "Accounts Payable", liabilityGroup.Id), CancellationToken.None);
        var rent = await new CreateAccountCommandHandler(db, numberGenerator).Handle(
            new CreateAccountCommand(organizationId, "Rent Expense", expenseGroup.Id), CancellationToken.None);

        return (organizationId, cash.Id, sales.Id, ap.Id, rent.Id);
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
