using ErpApp.Application.Accounting.Commands.CreateBankStatementLine;
using ErpApp.Application.Accounting.Commands.DeleteBankStatementLines;
using ErpApp.Application.Accounting.Queries.ListBankStatementLines;
using ErpApp.Application.Common.Exceptions;
using ErpApp.Application.Common.Filtering;
using ErpApp.Application.Common.Pagination;
using ErpApp.Application.Common.Persistence;
using ErpApp.Application.UnitTests.TestSupport;
using ErpApp.Domain.Accounting;
using FluentValidation;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.UnitTests.Accounting;

/// <summary>
/// Phase 55 -- the statement list and the two write paths, at handler level.
/// </summary>
public class BankStatementLineQueryTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 21, 10, 0, 0, TimeSpan.Zero);
    private static readonly Guid OrganizationId = Guid.NewGuid();
    private static readonly Guid OtherOrganizationId = Guid.NewGuid();

    [Fact]
    public async Task Lists_one_account_s_lines_newest_first()
    {
        var db = TestAppDbContext.Create();
        var (accountId, _) = await SeedAsync(db);

        var result = await ListAsync(db, accountId);

        Assert.Equal(3, result.TotalCount);
        Assert.Equal(["Interest", "ATM withdrawal", "Salary credit"], result.Items.Select(i => i.Description));

        // The display pair is derived from the one stored signed value, and exactly one side is
        // ever non-zero.
        var withdrawal = result.Items.Single(i => i.Description == "ATM withdrawal");
        Assert.Equal(0m, withdrawal.Deposit);
        Assert.Equal(250.75m, withdrawal.Withdrawal);
        Assert.Equal(-250.75m, withdrawal.SignedAmount);
    }

    /// <summary>
    /// The account is not an optional filter, so a second account's lines are not merely sorted
    /// after this one's -- they are absent. Seeded on the same tenant, because a cross-tenant
    /// assertion would pass on the OrganizationId filter alone and prove nothing about the
    /// account.
    /// </summary>
    [Fact]
    public async Task Another_account_s_lines_are_not_in_this_account_s_statement()
    {
        var db = TestAppDbContext.Create();
        var (accountId, otherAccountId) = await SeedAsync(db);

        Assert.Equal(3, (await ListAsync(db, accountId)).TotalCount);
        Assert.Equal(1, (await ListAsync(db, otherAccountId)).TotalCount);
    }

    [Fact]
    public async Task Another_tenant_s_lines_are_invisible()
    {
        var db = TestAppDbContext.Create();
        var (accountId, _) = await SeedAsync(db);

        db.BankStatementLines.Add(BankStatementLine.Create(
            OtherOrganizationId, accountId, new DateOnly(2026, 9, 9), "Not ours",
            StatementAmount.Deposit(99m), null, Now));
        await db.SaveChangesAsync();

        Assert.DoesNotContain((await ListAsync(db, accountId)).Items, i => i.Description == "Not ours");
    }

    [Fact]
    public async Task Filters_by_date_range_search_and_sort()
    {
        var db = TestAppDbContext.Create();
        var (accountId, _) = await SeedAsync(db);

        var searched = await ListAsync(db, accountId, search: "ATM");
        Assert.Equal(["ATM withdrawal"], searched.Items.Select(i => i.Description));

        var ranged = await ListAsync(db, accountId, from: new DateOnly(2026, 9, 2), to: new DateOnly(2026, 9, 3));
        Assert.Equal(2, ranged.TotalCount);

        var byDate = await ListAsync(db, accountId, sort: ListSort.DocumentDate);
        Assert.Equal(
            [new DateOnly(2026, 9, 3), new DateOnly(2026, 9, 2), new DateOnly(2026, 9, 1)],
            byDate.Items.Select(i => i.Date));
    }

    [Fact]
    public async Task A_missing_account_is_a_404_rather_than_an_empty_list()
    {
        var db = TestAppDbContext.Create();
        await SeedAsync(db);

        await Assert.ThrowsAsync<NotFoundException>(() => ListAsync(db, Guid.NewGuid()));
    }

    [Fact]
    public async Task Deletes_the_named_lines_and_leaves_the_rest()
    {
        var db = TestAppDbContext.Create();
        var (accountId, _) = await SeedAsync(db);
        var target = await db.BankStatementLines.FirstAsync(x => x.Description == "Interest");

        var result = await new DeleteBankStatementLinesCommandHandler(db).Handle(
            new DeleteBankStatementLinesCommand(OrganizationId, accountId, [target.Id], null),
            CancellationToken.None);

        Assert.Equal(1, result.DeletedCount);
        Assert.Equal(2, (await ListAsync(db, accountId)).TotalCount);
    }

    /// <summary>
    /// The answer to a file uploaded twice. A bank statement has no natural key, so nothing could
    /// have refused the second upload; what makes the mistake cheap is that every line records the
    /// run that created it.
    /// </summary>
    [Fact]
    public async Task Deletes_a_whole_import_run()
    {
        var db = TestAppDbContext.Create();
        var (accountId, _) = await SeedAsync(db);
        var importJobId = Guid.NewGuid();

        db.BankStatementLines.AddRange(
            BankStatementLine.Create(OrganizationId, accountId, new DateOnly(2026, 9, 10), "Dup A",
                StatementAmount.Deposit(10m), importJobId, Now),
            BankStatementLine.Create(OrganizationId, accountId, new DateOnly(2026, 9, 11), "Dup B",
                StatementAmount.Deposit(20m), importJobId, Now));
        await db.SaveChangesAsync();

        var result = await new DeleteBankStatementLinesCommandHandler(db).Handle(
            new DeleteBankStatementLinesCommand(OrganizationId, accountId, null, importJobId),
            CancellationToken.None);

        Assert.Equal(2, result.DeletedCount);
        Assert.Equal(3, (await ListAsync(db, accountId)).TotalCount);
    }

    /// <summary>
    /// A delete that names ids belonging to another account of the same tenant removes nothing and
    /// says so. Both halves of the handler's filter are load-bearing, and only the account half is
    /// exercised here -- the OrganizationId half would hide a bug in it.
    /// </summary>
    [Fact]
    public async Task A_delete_cannot_reach_another_account_s_lines()
    {
        var db = TestAppDbContext.Create();
        var (accountId, otherAccountId) = await SeedAsync(db);
        var other = await db.BankStatementLines.FirstAsync(x => x.BankAccountId == otherAccountId);

        await Assert.ThrowsAsync<NotFoundException>(() =>
            new DeleteBankStatementLinesCommandHandler(db).Handle(
                new DeleteBankStatementLinesCommand(OrganizationId, accountId, [other.Id], null),
                CancellationToken.None));

        Assert.Equal(1, (await ListAsync(db, otherAccountId)).TotalCount);
    }

    /// <summary>Neither selector would delete the whole account's statement; both at once has no
    /// meaning the caller could have intended.</summary>
    [Theory]
    [InlineData(false, false)]
    [InlineData(true, true)]
    public void A_delete_supplies_exactly_one_selector(bool lineIds, bool importJobId)
    {
        var command = new DeleteBankStatementLinesCommand(
            OrganizationId,
            Guid.NewGuid(),
            lineIds ? [Guid.NewGuid()] : null,
            importJobId ? Guid.NewGuid() : null);

        Assert.False(new DeleteBankStatementLinesCommandValidator().Validate(command).IsValid);
    }

    [Fact]
    public async Task A_statement_line_cannot_be_written_against_an_ordinary_account()
    {
        var db = TestAppDbContext.Create();
        await SeedAsync(db);
        var revenue = Account.Create(
            OrganizationId, "4000", "Sales Revenue", AccountRootType.Income, Guid.NewGuid(), AccountKind.Other);
        db.Accounts.Add(revenue);
        await db.SaveChangesAsync();

        await Assert.ThrowsAsync<ValidationException>(() =>
            new CreateBankStatementLineCommandHandler(db, TimeProvider.System).Handle(
                new CreateBankStatementLineCommand(
                    OrganizationId, revenue.Id, new DateOnly(2026, 9, 1), "nope", 100m, 0m, null),
                CancellationToken.None));
    }

    private static Task<PagedResult<BankStatementLineListItem>> ListAsync(
        IAppDbContext db,
        Guid accountId,
        string? search = null,
        DateOnly? from = null,
        DateOnly? to = null,
        string? sort = null) =>
        new ListBankStatementLinesQueryHandler(db).Handle(
            new ListBankStatementLinesQuery(OrganizationId, accountId, 1, 50, search, from, to, sort),
            CancellationToken.None);

    private static async Task<(Guid AccountId, Guid OtherAccountId)> SeedAsync(IAppDbContext db)
    {
        var account = Account.Create(
            OrganizationId, "BC0001", "Nabil Bank", AccountRootType.Asset, Guid.NewGuid(), AccountKind.Bank);
        var other = Account.Create(
            OrganizationId, "BC0002", "Cash In Hand", AccountRootType.Asset, Guid.NewGuid(), AccountKind.Cash);
        db.Accounts.AddRange(account, other);

        // CreatedAt ascends with the date here so the two orderings are distinguishable but the
        // expectations stay readable.
        db.BankStatementLines.AddRange(
            BankStatementLine.Create(OrganizationId, account.Id, new DateOnly(2026, 9, 1), "Salary credit",
                StatementAmount.Deposit(1500m), null, Now),
            BankStatementLine.Create(OrganizationId, account.Id, new DateOnly(2026, 9, 2), "ATM withdrawal",
                StatementAmount.Withdrawal(250.75m), null, Now.AddMinutes(1)),
            BankStatementLine.Create(OrganizationId, account.Id, new DateOnly(2026, 9, 3), "Interest",
                StatementAmount.Deposit(12.5m), null, Now.AddMinutes(2)),
            BankStatementLine.Create(OrganizationId, other.Id, new DateOnly(2026, 9, 4), "Petty cash",
                StatementAmount.Withdrawal(40m), null, Now.AddMinutes(3)));

        await db.SaveChangesAsync();
        return (account.Id, other.Id);
    }
}
