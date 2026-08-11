using ErpApp.Application.Accounting;
using ErpApp.Application.Accounting.Commands.ApproveJournalVoucher;
using ErpApp.Application.Accounting.Commands.CreateJournalVoucher;
using ErpApp.Application.Accounting.Posting;
using ErpApp.Application.Common.Exceptions;
using ErpApp.Application.UnitTests.TestSupport;
using ErpApp.Domain.Accounting;
using ErpApp.Domain.Common;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.UnitTests.Accounting;

public class ApproveJournalVoucherCommandHandlerTests
{
    [Fact]
    public async Task Handle_assigns_a_real_number_and_posts_a_balanced_gl_entry()
    {
        var db = TestAppDbContext.Create();
        var (organizationId, cashAccountId, salesAccountId) = await AccountingTestSeed.SeedTwoAccountsAsync(db);
        var created = await new CreateJournalVoucherCommandHandler(db).Handle(
            new CreateJournalVoucherCommand(
                organizationId, new DateOnly(2026, 1, 1), null,
                [new JournalVoucherLineInput(cashAccountId, 1000m, 0m), new JournalVoucherLineInput(salesAccountId, 0m, 1000m)]),
            CancellationToken.None);
        var approverId = Guid.NewGuid();
        var handler = new ApproveJournalVoucherCommandHandler(
            db, new FakeDocumentNumberGenerator(), new FakeCurrentUserService(approverId), new JournalVoucherPostingRule());

        var result = await handler.Handle(new ApproveJournalVoucherCommand(organizationId, created.Id), CancellationToken.None);

        Assert.Equal(JournalVoucherStatus.Approved, result.Status);
        Assert.NotEqual(JournalVoucher.DraftCode, result.Code);

        var glEntry = await db.GlJournalEntries.Include(x => x.Lines).SingleAsync(
            x => x.SourceDocumentType == DocumentType.JournalVoucher && x.SourceDocumentId == created.Id);
        Assert.Equal(2, glEntry.Lines.Count);
        Assert.Equal(1000m, glEntry.Lines.Sum(x => x.Debit));
        Assert.Equal(glEntry.Lines.Sum(x => x.Debit), glEntry.Lines.Sum(x => x.Credit));
    }

    [Fact]
    public async Task Handle_throws_conflict_when_unbalanced()
    {
        var db = TestAppDbContext.Create();
        var (organizationId, cashAccountId, salesAccountId) = await AccountingTestSeed.SeedTwoAccountsAsync(db);
        var created = await new CreateJournalVoucherCommandHandler(db).Handle(
            new CreateJournalVoucherCommand(
                organizationId, new DateOnly(2026, 1, 1), null,
                [new JournalVoucherLineInput(cashAccountId, 1000m, 0m), new JournalVoucherLineInput(salesAccountId, 0m, 900m)]),
            CancellationToken.None);
        var handler = new ApproveJournalVoucherCommandHandler(
            db, new FakeDocumentNumberGenerator(), new FakeCurrentUserService(Guid.NewGuid()), new JournalVoucherPostingRule());

        await Assert.ThrowsAsync<ConflictException>(() => handler.Handle(
            new ApproveJournalVoucherCommand(organizationId, created.Id), CancellationToken.None));
    }

    [Fact]
    public async Task Handle_throws_conflict_when_fewer_than_two_lines()
    {
        var db = TestAppDbContext.Create();
        var (organizationId, cashAccountId, _) = await AccountingTestSeed.SeedTwoAccountsAsync(db);
        var created = await new CreateJournalVoucherCommandHandler(db).Handle(
            new CreateJournalVoucherCommand(
                organizationId, new DateOnly(2026, 1, 1), null, [new JournalVoucherLineInput(cashAccountId, 1000m, 0m)]),
            CancellationToken.None);
        var handler = new ApproveJournalVoucherCommandHandler(
            db, new FakeDocumentNumberGenerator(), new FakeCurrentUserService(Guid.NewGuid()), new JournalVoucherPostingRule());

        await Assert.ThrowsAsync<ConflictException>(() => handler.Handle(
            new ApproveJournalVoucherCommand(organizationId, created.Id), CancellationToken.None));
    }
}
