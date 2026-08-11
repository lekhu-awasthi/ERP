using ErpApp.Application.Accounting;
using ErpApp.Application.Accounting.Commands.ApproveJournalVoucher;
using ErpApp.Application.Accounting.Commands.CreateJournalVoucher;
using ErpApp.Application.Accounting.Commands.UpdateJournalVoucher;
using ErpApp.Application.Accounting.Posting;
using ErpApp.Application.Common.Exceptions;
using ErpApp.Application.UnitTests.TestSupport;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.UnitTests.Accounting;

public class UpdateJournalVoucherCommandHandlerTests
{
    // Each test opens a fresh TestAppDbContext (same InMemory database name) per handler call --
    // mirrors the real Api's one-DbContext-per-request pattern, so the Update handler's
    // Include(x => x.Lines) reads a clean snapshot rather than merging into entities the earlier
    // Create call already has tracked in memory (which confused the InMemory provider's
    // orphan-deletion tracking for the Clear+re-Add below).

    [Fact]
    public async Task Handle_replaces_the_entire_line_set()
    {
        var dbName = Guid.NewGuid().ToString();
        var db1 = TestAppDbContext.Create(dbName);
        var (organizationId, cashAccountId, salesAccountId) = await AccountingTestSeed.SeedTwoAccountsAsync(db1);
        var created = await new CreateJournalVoucherCommandHandler(db1).Handle(
            new CreateJournalVoucherCommand(
                organizationId, new DateOnly(2026, 1, 1), "Cash sale",
                [new JournalVoucherLineInput(cashAccountId, 1000m, 0m), new JournalVoucherLineInput(salesAccountId, 0m, 1000m)]),
            CancellationToken.None);

        var db2 = TestAppDbContext.Create(dbName);
        var handler = new UpdateJournalVoucherCommandHandler(db2);
        await handler.Handle(
            new UpdateJournalVoucherCommand(
                organizationId, created.Id, new DateOnly(2026, 1, 2), "Revised",
                [new JournalVoucherLineInput(cashAccountId, 500m, 0m), new JournalVoucherLineInput(salesAccountId, 0m, 500m)]),
            CancellationToken.None);

        var db3 = TestAppDbContext.Create(dbName);
        var journalVoucher = await db3.JournalVouchers.Include(x => x.Lines).SingleAsync(x => x.Id == created.Id);
        Assert.Equal(2, journalVoucher.Lines.Count);
        Assert.All(journalVoucher.Lines, l => Assert.True(l.Debit == 500m || l.Credit == 500m));
        Assert.Equal("Revised", journalVoucher.Reference);
    }

    [Fact]
    public async Task Handle_throws_conflict_once_approved()
    {
        var dbName = Guid.NewGuid().ToString();
        var db1 = TestAppDbContext.Create(dbName);
        var (organizationId, cashAccountId, salesAccountId) = await AccountingTestSeed.SeedTwoAccountsAsync(db1);
        var created = await new CreateJournalVoucherCommandHandler(db1).Handle(
            new CreateJournalVoucherCommand(
                organizationId, new DateOnly(2026, 1, 1), null,
                [new JournalVoucherLineInput(cashAccountId, 1000m, 0m), new JournalVoucherLineInput(salesAccountId, 0m, 1000m)]),
            CancellationToken.None);

        var db2 = TestAppDbContext.Create(dbName);
        await new ApproveJournalVoucherCommandHandler(
                db2, new FakeDocumentNumberGenerator(), new FakeCurrentUserService(Guid.NewGuid()), new JournalVoucherPostingRule())
            .Handle(new ApproveJournalVoucherCommand(organizationId, created.Id), CancellationToken.None);

        var db3 = TestAppDbContext.Create(dbName);
        var handler = new UpdateJournalVoucherCommandHandler(db3);

        await Assert.ThrowsAsync<ConflictException>(() => handler.Handle(
            new UpdateJournalVoucherCommand(
                organizationId, created.Id, new DateOnly(2026, 1, 2), null,
                [new JournalVoucherLineInput(cashAccountId, 500m, 0m), new JournalVoucherLineInput(salesAccountId, 0m, 500m)]),
            CancellationToken.None));
    }
}
