using ErpApp.Application.Accounting.Commands.CreateOrUpdateOpeningBalanceLine;
using ErpApp.Application.UnitTests.TestSupport;
using ErpApp.Domain.Common;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.UnitTests.Accounting;

public class CreateOrUpdateOpeningBalanceLineCommandHandlerTests
{
    [Fact]
    public async Task Handle_posts_a_balanced_gl_entry_against_an_auto_provisioned_equity_account()
    {
        var db = TestAppDbContext.Create();
        var (organizationId, cashAccountId, _) = await AccountingTestSeed.SeedTwoAccountsAsync(db);
        var handler = new CreateOrUpdateOpeningBalanceLineCommandHandler(db, new FakeDocumentNumberGenerator());

        var result = await handler.Handle(
            new CreateOrUpdateOpeningBalanceLineCommand(organizationId, cashAccountId, 1000m, 0m), CancellationToken.None);

        Assert.Equal(1000m, result.Debit);
        var equityAccount = await db.Accounts.SingleAsync(x => x.Name == "Opening Balance Equity");
        var entry = await db.GlJournalEntries.Include(x => x.Lines)
            .SingleAsync(x => x.SourceDocumentType == DocumentType.OpeningBalance && x.SourceDocumentId == result.Id);
        Assert.Equal(2, entry.Lines.Count);
        Assert.Contains(entry.Lines, l => l.AccountId == cashAccountId && l.Debit == 1000m);
        Assert.Contains(entry.Lines, l => l.AccountId == equityAccount.Id && l.Credit == 1000m);
    }

    [Fact]
    public async Task Handle_reverses_the_prior_posting_when_correcting_an_existing_line()
    {
        var db = TestAppDbContext.Create();
        var (organizationId, cashAccountId, _) = await AccountingTestSeed.SeedTwoAccountsAsync(db);
        var handler = new CreateOrUpdateOpeningBalanceLineCommandHandler(db, new FakeDocumentNumberGenerator());
        var first = await handler.Handle(
            new CreateOrUpdateOpeningBalanceLineCommand(organizationId, cashAccountId, 1000m, 0m), CancellationToken.None);

        var second = await handler.Handle(
            new CreateOrUpdateOpeningBalanceLineCommand(organizationId, cashAccountId, 1500m, 0m), CancellationToken.None);

        Assert.Equal(first.Id, second.Id);
        Assert.Equal(1500m, second.Debit);

        var entries = await db.GlJournalEntries.Include(x => x.Lines)
            .Where(x => x.SourceDocumentType == DocumentType.OpeningBalance && x.SourceDocumentId == first.Id)
            .ToListAsync();
        Assert.Equal(3, entries.Count); // original post + reversal + corrected post

        var netCashDebit = entries.SelectMany(e => e.Lines).Where(l => l.AccountId == cashAccountId).Sum(l => l.Debit - l.Credit);
        Assert.Equal(1500m, netCashDebit);
    }

    [Fact]
    public async Task Handle_reuses_the_same_equity_account_across_multiple_lines()
    {
        var db = TestAppDbContext.Create();
        var (organizationId, cashAccountId, salesAccountId) = await AccountingTestSeed.SeedTwoAccountsAsync(db);
        var handler = new CreateOrUpdateOpeningBalanceLineCommandHandler(db, new FakeDocumentNumberGenerator());

        await handler.Handle(new CreateOrUpdateOpeningBalanceLineCommand(organizationId, cashAccountId, 1000m, 0m), CancellationToken.None);
        await handler.Handle(new CreateOrUpdateOpeningBalanceLineCommand(organizationId, salesAccountId, 0m, 500m), CancellationToken.None);

        var equityAccountCount = await db.Accounts.CountAsync(x => x.Name == "Opening Balance Equity");
        Assert.Equal(1, equityAccountCount);
    }
}
