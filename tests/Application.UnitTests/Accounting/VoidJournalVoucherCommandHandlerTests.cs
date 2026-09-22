using ErpApp.Application.Accounting.Cash;
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
/// right place to prove the reversal's own net-to-zero claim end to end through a
/// real handler, not just via the Domain-level unit test. Later Void handlers (PurchaseBill,
/// Invoice, etc.) reuse the exact same SourceDocumentGlEntries call, so this is the
/// shared-mechanism test.
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
            db, new FakeDocumentNumberGenerator(), new FakeCurrentUserService(Guid.NewGuid()), new JournalVoucherPostingRule(), new GlCashBalancePolicy(db))
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

    /// <summary>
    /// Phase 57 -- a document whose posting has been matched into a bank reconciliation cannot be
    /// voided. The refusal lives in <c>SourceDocumentGlEntries.ReverseOutstandingAsync</c>, which is
    /// the single path every reversal in the product takes, so proving it here proves it for all
    /// thirteen voids rather than for this one.
    ///
    /// <para>It is a <b>refusal</b> and not a cascade, which is phase 56's own choice applied to the
    /// mirror case: 56 refuses to delete a reconciled statement line, because a reversal that left
    /// the reconciliation alive would go on asserting agreement between a bank line and a movement
    /// that had been backed out. Unreconcile releases both sides in one click, so the user is told
    /// which order to do it in rather than blocked.</para>
    /// </summary>
    [Fact]
    public async Task Handle_refuses_to_void_a_document_whose_posting_is_reconciled()
    {
        var db = TestAppDbContext.Create();
        var (organizationId, cashAccountId, salesAccountId) = await AccountingTestSeed.SeedTwoAccountsAsync(db);
        var created = await new CreateJournalVoucherCommandHandler(db).Handle(
            new CreateJournalVoucherCommand(
                organizationId, new DateOnly(2026, 1, 1), null,
                [new JournalVoucherLineInput(cashAccountId, 1000m, 0m), new JournalVoucherLineInput(salesAccountId, 0m, 1000m)]),
            CancellationToken.None);
        await new ApproveJournalVoucherCommandHandler(
            db, new FakeDocumentNumberGenerator(), new FakeCurrentUserService(Guid.NewGuid()), new JournalVoucherPostingRule(), new GlCashBalancePolicy(db))
            .Handle(new ApproveJournalVoucherCommand(organizationId, created.Id), CancellationToken.None);

        var cashLine = await db.GlLines.SingleAsync(x => x.AccountId == cashAccountId);
        cashLine.Reconcile(Guid.NewGuid());
        await db.SaveChangesAsync();

        var error = await Assert.ThrowsAsync<ConflictException>(() =>
            new VoidJournalVoucherCommandHandler(db, new FakeCurrentUserService(Guid.NewGuid()))
                .Handle(new VoidJournalVoucherCommand(organizationId, created.Id), CancellationToken.None));

        Assert.Contains("Unreconcile", error.Message, StringComparison.Ordinal);

        // Nothing was reversed and the document is still Approved -- the refusal has to be complete,
        // or it would leave a half-voided document behind the error message.
        var entries = await db.GlJournalEntries
            .Where(x => x.SourceDocumentType == DocumentType.JournalVoucher && x.SourceDocumentId == created.Id)
            .CountAsync();

        Assert.Equal(1, entries);

        // Read past the change tracker on purpose. The handler marks the aggregate Void *before* it
        // reverses -- as every one of the thirteen does -- so the tracked instance is dirty when the
        // refusal throws. Nothing is persisted, because the handler's single SaveChangesAsync is
        // never reached and the request's DbContext dies with its scope; the claim being made here
        // is about the stored row, so the query has to be the one that reads it.
        Assert.Equal(
            JournalVoucherStatus.Approved,
            (await db.JournalVouchers.AsNoTracking().SingleAsync(x => x.Id == created.Id)).Status);
    }

    /// <summary>
    /// The same document once the reconciliation has been released. Asserted because the guard is
    /// only acceptable if the way out of it actually works -- a refusal with no exit would be a
    /// document that can never be corrected.
    /// </summary>
    [Fact]
    public async Task Handle_voids_normally_once_the_reconciliation_is_released()
    {
        var db = TestAppDbContext.Create();
        var (organizationId, cashAccountId, salesAccountId) = await AccountingTestSeed.SeedTwoAccountsAsync(db);
        var created = await new CreateJournalVoucherCommandHandler(db).Handle(
            new CreateJournalVoucherCommand(
                organizationId, new DateOnly(2026, 1, 1), null,
                [new JournalVoucherLineInput(cashAccountId, 1000m, 0m), new JournalVoucherLineInput(salesAccountId, 0m, 1000m)]),
            CancellationToken.None);
        await new ApproveJournalVoucherCommandHandler(
            db, new FakeDocumentNumberGenerator(), new FakeCurrentUserService(Guid.NewGuid()), new JournalVoucherPostingRule(), new GlCashBalancePolicy(db))
            .Handle(new ApproveJournalVoucherCommand(organizationId, created.Id), CancellationToken.None);

        var cashLine = await db.GlLines.SingleAsync(x => x.AccountId == cashAccountId);
        cashLine.Reconcile(Guid.NewGuid());
        await db.SaveChangesAsync();

        cashLine.ReleaseFromReconciliation();
        await db.SaveChangesAsync();

        var result = await new VoidJournalVoucherCommandHandler(db, new FakeCurrentUserService(Guid.NewGuid()))
            .Handle(new VoidJournalVoucherCommand(organizationId, created.Id), CancellationToken.None);

        Assert.Equal(JournalVoucherStatus.Void, result.Status);
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
            db, new FakeDocumentNumberGenerator(), new FakeCurrentUserService(Guid.NewGuid()), new JournalVoucherPostingRule(), new GlCashBalancePolicy(db))
            .Handle(new ApproveJournalVoucherCommand(organizationId, created.Id), CancellationToken.None);
        var handler = new VoidJournalVoucherCommandHandler(db, new FakeCurrentUserService(Guid.NewGuid()));
        await handler.Handle(new VoidJournalVoucherCommand(organizationId, created.Id), CancellationToken.None);

        await Assert.ThrowsAsync<ConflictException>(() => handler.Handle(
            new VoidJournalVoucherCommand(organizationId, created.Id), CancellationToken.None));
    }
}
