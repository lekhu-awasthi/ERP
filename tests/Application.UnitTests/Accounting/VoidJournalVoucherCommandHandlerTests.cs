using ErpApp.Application.Accounting;
using ErpApp.Application.Accounting.Commands.ApproveJournalVoucher;
using ErpApp.Application.Accounting.Commands.CreateJournalVoucher;
using ErpApp.Application.Accounting.Commands.VoidJournalVoucher;
using ErpApp.Application.Accounting.Posting;
using ErpApp.Application.Common.Exceptions;
using ErpApp.Application.UnitTests.TestSupport;
using ErpApp.Domain.Accounting;
using ErpApp.Domain.Common;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.UnitTests.Accounting;

/// <summary>
/// JournalVoucher is the simplest Void case (GL reversal only, no stock, no dependents) -- the
/// right place to prove GlJournalEntry.PostReversalOf's own net-to-zero claim end to end through a
/// real handler, not just via the Domain-level unit test. Later Void handlers (PurchaseBill,
/// Invoice, etc.) reuse the exact same PostReversalOf call, so this is the shared-mechanism test.
/// </summary>
public class VoidJournalVoucherCommandHandlerTests
{
    [Fact]
    public async Task Handle_posts_a_mirror_reversal_that_nets_every_touched_account_to_zero()
    {
        var db = TestAppDbContext.Create();
        var (organizationId, cashAccountId, salesAccountId) = await AccountingTestSeed.SeedTwoAccountsAsync(db);
        var created = await new CreateJournalVoucherCommandHandler(db).Handle(
            new CreateJournalVoucherCommand(
                organizationId, new DateOnly(2026, 1, 1), null,
                [new JournalVoucherLineInput(cashAccountId, 1000m, 0m), new JournalVoucherLineInput(salesAccountId, 0m, 1000m)]),
            CancellationToken.None);
        await new ApproveJournalVoucherCommandHandler(
            db, new FakeDocumentNumberGenerator(), new FakeCurrentUserService(Guid.NewGuid()), new JournalVoucherPostingRule())
            .Handle(new ApproveJournalVoucherCommand(organizationId, created.Id), CancellationToken.None);

        var voiderId = Guid.NewGuid();
        var result = await new VoidJournalVoucherCommandHandler(db, new FakeCurrentUserService(voiderId))
            .Handle(new VoidJournalVoucherCommand(organizationId, created.Id), CancellationToken.None);

        Assert.Equal(JournalVoucherStatus.Void, result.Status);
        Assert.NotNull(result.VoidedAt);

        var entries = await db.GlJournalEntries.Include(x => x.Lines)
            .Where(x => x.SourceDocumentType == DocumentType.JournalVoucher && x.SourceDocumentId == created.Id)
            .ToListAsync();
        Assert.Equal(2, entries.Count);

        var netByAccount = entries.SelectMany(x => x.Lines)
            .GroupBy(x => x.AccountId)
            .ToDictionary(g => g.Key, g => g.Sum(x => x.Debit) - g.Sum(x => x.Credit));
        Assert.All(netByAccount.Values, net => Assert.Equal(0m, net));
    }

    [Fact]
    public async Task Handle_throws_conflict_when_voucher_is_still_draft()
    {
        var db = TestAppDbContext.Create();
        var (organizationId, cashAccountId, salesAccountId) = await AccountingTestSeed.SeedTwoAccountsAsync(db);
        var created = await new CreateJournalVoucherCommandHandler(db).Handle(
            new CreateJournalVoucherCommand(
                organizationId, new DateOnly(2026, 1, 1), null,
                [new JournalVoucherLineInput(cashAccountId, 1000m, 0m), new JournalVoucherLineInput(salesAccountId, 0m, 1000m)]),
            CancellationToken.None);

        await Assert.ThrowsAsync<ConflictException>(() => new VoidJournalVoucherCommandHandler(db, new FakeCurrentUserService(Guid.NewGuid()))
            .Handle(new VoidJournalVoucherCommand(organizationId, created.Id), CancellationToken.None));
    }

    [Fact]
    public async Task Handle_throws_conflict_when_voucher_is_already_void()
    {
        var db = TestAppDbContext.Create();
        var (organizationId, cashAccountId, salesAccountId) = await AccountingTestSeed.SeedTwoAccountsAsync(db);
        var created = await new CreateJournalVoucherCommandHandler(db).Handle(
            new CreateJournalVoucherCommand(
                organizationId, new DateOnly(2026, 1, 1), null,
                [new JournalVoucherLineInput(cashAccountId, 1000m, 0m), new JournalVoucherLineInput(salesAccountId, 0m, 1000m)]),
            CancellationToken.None);
        await new ApproveJournalVoucherCommandHandler(
            db, new FakeDocumentNumberGenerator(), new FakeCurrentUserService(Guid.NewGuid()), new JournalVoucherPostingRule())
            .Handle(new ApproveJournalVoucherCommand(organizationId, created.Id), CancellationToken.None);
        var handler = new VoidJournalVoucherCommandHandler(db, new FakeCurrentUserService(Guid.NewGuid()));
        await handler.Handle(new VoidJournalVoucherCommand(organizationId, created.Id), CancellationToken.None);

        await Assert.ThrowsAsync<ConflictException>(() => handler.Handle(
            new VoidJournalVoucherCommand(organizationId, created.Id), CancellationToken.None));
    }
}
